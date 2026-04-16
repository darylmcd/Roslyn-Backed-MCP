using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
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

        var (root, semanticModel, text, document, solution) =
            await ResolveDocumentAsync(workspaceId, filePath, ct).ConfigureAwait(false);

        var selectionSpan = BuildSelectionSpan(text, startLine, startColumn, endLine, endColumn);

        var (enclosingMember, statementsInSelection, parentBlock) =
            FindEnclosingMethodAndStatements(root, selectionSpan);

        var (parameters, flowsOut, variablesDeclaredInRegion, isStatic) =
            AnalyzeFlowAndInferSignature(semanticModel, statementsInSelection, enclosingMember);

        var (newMethod, callStatement) =
            BuildMethodAndCallSite(methodName, parameters, flowsOut, variablesDeclaredInRegion, isStatic, statementsInSelection);

        var newRoot = ReplaceStatementsAndInsertMethod(
            root, parentBlock, statementsInSelection, callStatement, newMethod, enclosingMember);

        // dr-9-7-produces-output-that-violates-project-formatting +
        // dr-9-9-format-bug-004-produces-malformed-body-closing-b: route the synthesized
        // method declaration AND the synthesized call statement through Roslyn's
        // `Formatter.FormatAsync` so editorconfig-driven indentation, spacing, and
        // brace placement are applied. Both the new method and the call statement
        // carry `Formatter.Annotation`; the formatter touches only those nodes plus
        // their immediate context, leaving the rest of the document untouched.
        var documentWithEdit = document.WithSyntaxRoot(newRoot);
        var formattedDocument = await Formatter.FormatAsync(
            documentWithEdit,
            Formatter.Annotation,
            options: null,
            cancellationToken: ct).ConfigureAwait(false);
        var formattedRoot = await formattedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Formatted document produced no syntax root.");
        var newSolution = solution.WithDocumentSyntaxRoot(document.Id, formattedRoot);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {statementsInSelection.Count} statement(s) into method '{methodName}'";
        var token = _previewStore.Store(
            // Item #4 — pass changes so the store knows whether the diff was truncated.
            workspaceId, newSolution,
            _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogDebug("Extract method preview: {Description}", description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private async Task<(CompilationUnitSyntax Root, SemanticModel Model, Microsoft.CodeAnalysis.Text.SourceText Text, Document Document, Solution Solution)>
        ResolveDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        return (root, semanticModel, text, document, solution);
    }

    private static Microsoft.CodeAnalysis.Text.TextSpan BuildSelectionSpan(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine, int startColumn, int endLine, int endColumn)
    {
        var startPosition = text.Lines[startLine - 1].Start + (startColumn - 1);
        var endPosition = text.Lines[endLine - 1].Start + (endColumn - 1);
        return Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startPosition, endPosition);
    }

    private static (MemberDeclarationSyntax EnclosingMember, List<StatementSyntax> Statements, BlockSyntax ParentBlock)
        FindEnclosingMethodAndStatements(CompilationUnitSyntax root, Microsoft.CodeAnalysis.Text.TextSpan selectionSpan)
    {
        var enclosingMember = root.FindNode(selectionSpan)
            .AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is MethodDeclarationSyntax)
            ?? throw new InvalidOperationException("Selection must be inside a method body.");

        var statementsInSelection = FindStatementsInSelection(enclosingMember, selectionSpan);
        if (statementsInSelection.Count == 0)
            throw new InvalidOperationException(
                "No complete statements found in selection. Select one or more complete statements.");

        var parentBlock = statementsInSelection[0].Parent as BlockSyntax;
        if (parentBlock is null || statementsInSelection.Any(s => s.Parent != parentBlock))
            throw new InvalidOperationException(
                "All selected statements must be in the same block scope.");

        return (enclosingMember, statementsInSelection, parentBlock);
    }

    private static (List<(string Name, ITypeSymbol? Type)> Parameters, List<(string Name, ITypeSymbol? Type)> FlowsOut, HashSet<string> VariablesDeclaredInRegion, bool IsStatic)
        AnalyzeFlowAndInferSignature(
            SemanticModel semanticModel,
            List<StatementSyntax> statementsInSelection,
            MemberDeclarationSyntax enclosingMember)
    {
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
            throw new InvalidOperationException("Data flow analysis did not succeed for the selected region.");

        var controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);
        if (controlFlow is null || !controlFlow.Succeeded)
            throw new InvalidOperationException("Control flow analysis did not succeed for the selected region.");

        if (controlFlow.ReturnStatements.Length > 0)
            throw new InvalidOperationException(
                "Cannot extract: the selection contains return statements. " +
                "Extract method requires a single-exit region without return statements.");

        // Item #9 — extract-method-preview-fabricates-this-parameter. `DataFlowsIn`
        // includes the implicit `this` pointer when the extracted region reads instance
        // state via an unqualified member access (e.g. `_field`). Roslyn models `this`
        // as an `IParameterSymbol` subtype (`IThisParameterSymbol`) whose Name is
        // literally "this" and Type is the containing type. Without the explicit
        // exclusion below, the filter `s is ILocalSymbol or IParameterSymbol` accepts
        // it, the rendered parameter list becomes `(MusicManager this, Audio item, …)`,
        // and the generated method fails to compile (audit: Jellyfin stress test §5).
        // The extracted method is always declared on the same containing type (isStatic
        // is derived from the enclosing member below), so `this` is implicitly available
        // — the exclusion is correct for every branch.
        static bool IsCapturableVariable(ISymbol s)
            => s is ILocalSymbol
               || (s is IParameterSymbol parameter && !parameter.IsThis);

        var parameters = dataFlow.DataFlowsIn
            .Where(IsCapturableVariable)
            .Select(s => (s.Name, Type: GetSymbolType(s)))
            .Where(p => p.Type is not null)
            .ToList();

        var flowsOut = dataFlow.DataFlowsOut
            .Where(IsCapturableVariable)
            .Select(s => (s.Name, Type: GetSymbolType(s)))
            .Where(p => p.Type is not null)
            .ToList();

        if (flowsOut.Count > 1)
            throw new InvalidOperationException(
                $"Cannot extract: {flowsOut.Count} variables flow out of the selection " +
                $"({string.Join(", ", flowsOut.Select(v => v.Name))}). " +
                "Extract method supports at most one output variable as a return value.");

        // extract-method-apply-var-redeclaration: VariablesDeclared lists symbols whose declaration
        // is INSIDE the extracted region. If the single flowsOut variable is in this set the call
        // site needs `var x = M(...)` to introduce it; if it's NOT in this set the variable was
        // declared in an enclosing scope and we must emit a plain assignment `x = M(...)` —
        // otherwise we shadow the existing local and produce CS0136 + CS0841.
        var variablesDeclaredInRegion = dataFlow.VariablesDeclared
            .Where(s => s is ILocalSymbol)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (flowsOut.Count == 1)
        {
            var outName = flowsOut[0].Name;
            var declaredInside = variablesDeclaredInRegion.Contains(outName);
            var alwaysAssigned = dataFlow.AlwaysAssigned.Any(s => s.Name == outName);

            // For an existing local that flows out, we must be sure the region writes a complete
            // value rather than mutating part of one (e.g., `x +=`). DataFlowsOut + AlwaysAssigned
            // tells us whether the region unconditionally produces a definite assignment for x.
            // Without AlwaysAssigned, generating `x = M(...)` could mask compound updates.
            if (!declaredInside && !alwaysAssigned)
            {
                throw new InvalidOperationException(
                    $"Cannot extract: variable '{outName}' flows out of the selection but is not always assigned. " +
                    "This typically indicates a compound write (e.g. `x +=`) that cannot be safely replaced with a plain assignment from the extracted method.");
            }
        }

        var isStatic = enclosingMember.Modifiers.Any(SyntaxKind.StaticKeyword);

        return (parameters, flowsOut, variablesDeclaredInRegion, isStatic);
    }

    private static (MethodDeclarationSyntax NewMethod, StatementSyntax CallStatement)
        BuildMethodAndCallSite(
            string methodName,
            List<(string Name, ITypeSymbol? Type)> parameters,
            List<(string Name, ITypeSymbol? Type)> flowsOut,
            HashSet<string> variablesDeclaredInRegion,
            bool isStatic,
            List<StatementSyntax> statementsInSelection)
    {
        var returnType = flowsOut.Count == 1
            ? SyntaxFactory.ParseTypeName(flowsOut[0].Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));

        var accessModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword);

        // dr-9-7 / dr-9-9: build the parameter list with raw factory nodes — no manual
        // trailing-space hacks. `Formatter.FormatAsync` (invoked on the final document
        // in PreviewExtractMethodAsync) inserts proper spacing per editorconfig.
        var parameterList = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(
                parameters.Select(p =>
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                        .WithType(SyntaxFactory.ParseTypeName(
                            p.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))))));

        // Build extracted method body. Statements are taken verbatim from the source —
        // their original trivia (including indentation and line breaks for multi-line
        // chains) is preserved. The block is wrapped in elastic braces so the formatter
        // re-indents the body to match the target class scope while keeping intra-statement
        // formatting intact (dr-9-9: closing-brace + body-indent regression).
        var extractedStatements = new List<StatementSyntax>(statementsInSelection);
        if (flowsOut.Count == 1)
        {
            extractedStatements.Add(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.IdentifierName(flowsOut[0].Name)));
        }

        var body = SyntaxFactory.Block(
            SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
            SyntaxFactory.List(extractedStatements),
            SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));

        // Use Formatter.Annotation so the document-wide Formatter.FormatAsync pass
        // (in PreviewExtractMethodAsync) re-flows whitespace inside this method —
        // including class-scope indentation on the declaration line and a clean
        // newline before the next sibling member's closing brace.
        var newMethod = SyntaxFactory.MethodDeclaration(returnType, SyntaxFactory.Identifier(methodName))
            .WithModifiers(SyntaxFactory.TokenList(
                isStatic
                    ? [accessModifier, SyntaxFactory.Token(SyntaxKind.StaticKeyword)]
                    : [accessModifier]))
            .WithParameterList(parameterList)
            .WithBody(body)
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Build call site with raw factory nodes; spacing around `=`, `,`, etc. is
        // handled by Formatter.FormatAsync via the Formatter.Annotation tag below.
        var arguments = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(
                parameters.Select(p =>
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(p.Name)))));

        var callExpression = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(methodName), arguments);

        StatementSyntax callStatement;
        if (flowsOut.Count == 1)
        {
            var outName = flowsOut[0].Name;
            // extract-method-apply-var-redeclaration: if the variable was declared OUTSIDE the
            // extracted region (not in dataFlow.VariablesDeclared), emit a plain assignment to
            // avoid CS0136 (variable shadowing) + CS0841 (use before declaration). Only synthesize
            // `var x = M(...)` when x is genuinely a new local introduced by the extracted region.
            if (variablesDeclaredInRegion.Contains(outName))
            {
                callStatement = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("var"),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(outName)
                                .WithInitializer(SyntaxFactory.EqualsValueClause(callExpression)))));
            }
            else
            {
                callStatement = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(outName),
                        callExpression));
            }
        }
        else
        {
            callStatement = SyntaxFactory.ExpressionStatement(callExpression);
        }

        // Preserve the leading trivia from the original first statement so the call site
        // sits at the correct column inside the enclosing block, but tag the node with
        // Formatter.Annotation so `Formatter.FormatAsync` corrects intra-statement
        // spacing (e.g., the `=` and `,` tokens) per editorconfig.
        callStatement = callStatement
            .WithLeadingTrivia(statementsInSelection[0].GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return (newMethod, callStatement);
    }

    private static CompilationUnitSyntax ReplaceStatementsAndInsertMethod(
        CompilationUnitSyntax root,
        BlockSyntax parentBlock,
        List<StatementSyntax> statementsInSelection,
        StatementSyntax callStatement,
        MethodDeclarationSyntax newMethod,
        MemberDeclarationSyntax enclosingMember)
    {
        // Replace selected statements with call site
        var newStatements = new List<SyntaxNode>();
        var replacedCallSite = false;

        foreach (var statement in parentBlock.Statements)
        {
            if (statementsInSelection.Contains(statement))
            {
                if (!replacedCallSite)
                {
                    newStatements.Add(callStatement);
                    replacedCallSite = true;
                }
            }
            else
            {
                newStatements.Add(statement);
            }
        }

        var newBlock = parentBlock.WithStatements(
            SyntaxFactory.List(newStatements.Cast<StatementSyntax>()));

        // Find the top-level member and enclosing type
        var topLevelMember = enclosingMember.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .LastOrDefault(m => m.Parent is TypeDeclarationSyntax)
            ?? enclosingMember;

        var typeDecl = topLevelMember.Parent as TypeDeclarationSyntax
            ?? throw new InvalidOperationException("Could not find the enclosing type declaration.");

        // Apply block replacement
        var newRoot = root.ReplaceNode(parentBlock, newBlock);

        // Re-find type in modified tree and insert new method
        var newTypeDecl = newRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == typeDecl.Identifier.Text);

        var topMemberIndex = typeDecl.Members.IndexOf(topLevelMember);
        if (topMemberIndex >= 0 && topMemberIndex < newTypeDecl.Members.Count)
        {
            var insertionPoint = newTypeDecl.Members[topMemberIndex];
            var memberIndex = newTypeDecl.Members.IndexOf(insertionPoint);
            var updatedMembers = newTypeDecl.Members.Insert(memberIndex + 1, newMethod);
            newRoot = newRoot.ReplaceNode(newTypeDecl, newTypeDecl.WithMembers(updatedMembers));
        }
        else
        {
            var updatedMembers = newTypeDecl.Members.Add(newMethod);
            newRoot = newRoot.ReplaceNode(newTypeDecl, newTypeDecl.WithMembers(updatedMembers));
        }

        // dependency-inversion-noisy-diff: avoid reformatting the entire compilation unit;
        // only the extracted method node was normalized earlier in the pipeline.
        return newRoot;
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
