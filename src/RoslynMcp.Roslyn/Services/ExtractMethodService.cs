using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Extracts a selection of statements into a new method, using Roslyn's DataFlowAnalysis
/// to infer parameters (DataFlowsIn) and return values (DataFlowsOut).
/// </summary>
public sealed class ExtractMethodService : IExtractMethodService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<ExtractMethodService> _logger;

    public ExtractMethodService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ILogger<ExtractMethodService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewExtractMethodAsync(
        string workspaceId, string filePath,
        int startLine, int startColumn, int endLine, int endColumn,
        string methodName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name must not be empty.", nameof(methodName));

        if (startLine > endLine || (startLine == endLine && startColumn > endColumn))
            throw new ArgumentException("Start position must be before end position.");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        // Build the selection span
        var startPosition = text.Lines[startLine - 1].Start + (startColumn - 1);
        var endPosition = text.Lines[endLine - 1].Start + (endColumn - 1);
        var selectionSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startPosition, endPosition);

        // Find the enclosing method
        var enclosingMember = root.FindNode(selectionSpan)
            .AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is MethodDeclarationSyntax)
            ?? throw new InvalidOperationException(
                "Selection must be inside a method body.");

        // Collect all statements that overlap the selection
        var statementsInSelection = FindStatementsInSelection(enclosingMember, selectionSpan);
        if (statementsInSelection.Count == 0)
            throw new InvalidOperationException(
                "No complete statements found in selection. Select one or more complete statements.");

        // Verify all statements share the same parent block
        var parentBlock = statementsInSelection[0].Parent;
        if (parentBlock is null || statementsInSelection.Any(s => s.Parent != parentBlock))
            throw new InvalidOperationException(
                "All selected statements must be in the same block scope.");

        // Data flow analysis for parameter and return value inference
        var firstStatement = statementsInSelection[0];
        var lastStatement = statementsInSelection[^1];

        DataFlowAnalysis? dataFlow;
        try
        {
            dataFlow = semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Data flow analysis failed: {ex.Message}. " +
                "Ensure the selection covers complete statements within a single block.", ex);
        }

        if (dataFlow is null || !dataFlow.Succeeded)
            throw new InvalidOperationException(
                "Data flow analysis did not succeed for the selected region.");

        // Control flow analysis to validate single-exit
        var controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);
        if (controlFlow is null || !controlFlow.Succeeded)
            throw new InvalidOperationException(
                "Control flow analysis did not succeed for the selected region.");

        if (controlFlow.ReturnStatements.Length > 0)
            throw new InvalidOperationException(
                "Cannot extract: the selection contains return statements. " +
                "Extract method requires a single-exit region without return statements.");

        // Determine parameters: variables that flow in from outside
        var parameters = dataFlow.DataFlowsIn
            .Where(s => s is ILocalSymbol or IParameterSymbol)
            .Select(s => (s.Name, Type: GetSymbolType(s)))
            .Where(p => p.Type is not null)
            .ToList();

        // Determine return: variables that flow out (used after the selection)
        var flowsOut = dataFlow.DataFlowsOut
            .Where(s => s is ILocalSymbol or IParameterSymbol)
            .Select(s => (s.Name, Type: GetSymbolType(s)))
            .Where(p => p.Type is not null)
            .ToList();

        if (flowsOut.Count > 1)
            throw new InvalidOperationException(
                $"Cannot extract: {flowsOut.Count} variables flow out of the selection " +
                $"({string.Join(", ", flowsOut.Select(v => v.Name))}). " +
                "Extract method supports at most one output variable as a return value.");

        // Determine method return type
        var returnType = flowsOut.Count == 1
            ? SyntaxFactory.ParseTypeName(flowsOut[0].Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));

        // Determine the access modifier from the enclosing member
        var accessModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword);

        // Determine if static
        var isStatic = enclosingMember.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Build the parameter list
        var parameterList = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(
                parameters.Select(p =>
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                        .WithType(SyntaxFactory.ParseTypeName(
                            p.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                            .WithTrailingTrivia(SyntaxFactory.Space)))));

        // Build the method body
        var extractedStatements = new List<StatementSyntax>(statementsInSelection);

        // If there's a return value, add a return statement at the end
        if (flowsOut.Count == 1)
        {
            extractedStatements.Add(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.IdentifierName(flowsOut[0].Name))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
        }

        var body = SyntaxFactory.Block(extractedStatements);

        // Build the new method declaration
        var newMethod = SyntaxFactory.MethodDeclaration(returnType, SyntaxFactory.Identifier(methodName))
            .WithModifiers(SyntaxFactory.TokenList(
                isStatic
                    ? [accessModifier, SyntaxFactory.Token(SyntaxKind.StaticKeyword)]
                    : [accessModifier]))
            .WithParameterList(parameterList)
            .WithBody(body)
            .NormalizeWhitespace();

        // Build the call expression
        var arguments = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(
                parameters.Select(p =>
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(p.Name)))));

        var callExpression = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(methodName), arguments);

        StatementSyntax callStatement;
        if (flowsOut.Count == 1)
        {
            // var x = NewMethod(args);
            callStatement = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var").WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(flowsOut[0].Name)
                            .WithInitializer(SyntaxFactory.EqualsValueClause(callExpression)))));
        }
        else
        {
            // NewMethod(args);
            callStatement = SyntaxFactory.ExpressionStatement(callExpression);
        }

        // Preserve the leading trivia of the first statement on the call site
        callStatement = callStatement
            .WithLeadingTrivia(firstStatement.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        // Replace the statements in the original code
        var newStatements = new List<SyntaxNode>();
        var parentBlockNode = (BlockSyntax)parentBlock!;
        var inSelection = false;
        var replacedCallSite = false;

        foreach (var statement in parentBlockNode.Statements)
        {
            if (statementsInSelection.Contains(statement))
            {
                if (!replacedCallSite)
                {
                    newStatements.Add(callStatement);
                    replacedCallSite = true;
                }
                inSelection = true;
            }
            else
            {
                newStatements.Add(statement);
                if (inSelection) inSelection = false;
            }
        }

        var newBlock = parentBlockNode.WithStatements(
            SyntaxFactory.List(newStatements.Cast<StatementSyntax>()));

        // Insert the new method after the enclosing member
        var topLevelMember = enclosingMember.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .LastOrDefault(m => m.Parent is TypeDeclarationSyntax)
            ?? enclosingMember;

        var typeDecl = topLevelMember.Parent as TypeDeclarationSyntax
            ?? throw new InvalidOperationException("Could not find the enclosing type declaration.");

        // Apply changes to the syntax tree
        var newRoot = root.ReplaceNode(parentBlockNode, newBlock);

        // Re-find the type declaration in the new tree
        var newTypeDecl = newRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == typeDecl.Identifier.Text);

        // Find the top-level member by position matching
        var topMemberIndex = typeDecl.Members.IndexOf(topLevelMember);
        if (topMemberIndex >= 0 && topMemberIndex < newTypeDecl.Members.Count)
        {
            var insertionPoint = newTypeDecl.Members[topMemberIndex];
            var memberIndex = newTypeDecl.Members.IndexOf(insertionPoint);
            var updatedMembers = newTypeDecl.Members.Insert(memberIndex + 1, newMethod);
            var updatedTypeDecl = newTypeDecl.WithMembers(updatedMembers);
            newRoot = newRoot.ReplaceNode(newTypeDecl, updatedTypeDecl);
        }
        else
        {
            // Fallback: add at the end of the type
            var updatedMembers = newTypeDecl.Members.Add(newMethod);
            var updatedTypeDecl = newTypeDecl.WithMembers(updatedMembers);
            newRoot = newRoot.ReplaceNode(newTypeDecl, updatedTypeDecl);
        }

        // Format the modified tree
        newRoot = newRoot.NormalizeWhitespace();

        // Build the new solution
        var newSolution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);

        // Compute diff and store preview
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {statementsInSelection.Count} statement(s) into method '{methodName}'";
        var token = _previewStore.Store(
            workspaceId, newSolution,
            _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogDebug("Extract method preview: {Description}", description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static List<StatementSyntax> FindStatementsInSelection(
        SyntaxNode enclosingMember, Microsoft.CodeAnalysis.Text.TextSpan selectionSpan)
    {
        return enclosingMember
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is BlockSyntax
                     && selectionSpan.Contains(s.Span))
            .OrderBy(s => s.SpanStart)
            .ToList();
    }

    private static ITypeSymbol? GetSymbolType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol param => param.Type,
        _ => null
    };
}
