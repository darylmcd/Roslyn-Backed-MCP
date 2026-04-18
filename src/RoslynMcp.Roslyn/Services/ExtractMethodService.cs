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

    // ============================================================================
    // extract_shared_expression_to_helper_preview
    // ----------------------------------------------------------------------------
    // Detects the expression at the example span, scans the enclosing type (or
    // project when allowCrossFile=true) for structurally-identical expressions, and
    // synthesizes a private static helper that every hit is rewritten to call.
    // See IExtractMethodService.PreviewExtractSharedExpressionToHelperAsync for the
    // full contract.
    // ============================================================================

    public async Task<RefactoringPreviewDto> PreviewExtractSharedExpressionToHelperAsync(
        string workspaceId,
        string exampleFilePath,
        int exampleStartLine, int exampleStartColumn,
        int exampleEndLine, int exampleEndColumn,
        string helperName,
        string helperAccessibility,
        bool allowCrossFile,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(helperName))
            throw new ArgumentException("Helper name must not be empty.", nameof(helperName));
        if (exampleStartLine > exampleEndLine
            || (exampleStartLine == exampleEndLine && exampleStartColumn > exampleEndColumn))
            throw new ArgumentException("Start position must be before end position.");

        var accessibilityToken = ParseAccessibility(helperAccessibility);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var exampleDocument = SymbolResolver.FindDocument(solution, exampleFilePath)
            ?? throw new InvalidOperationException($"Document not found: {exampleFilePath}");

        var exampleRoot = await exampleDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var exampleModel = await exampleDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created for the example document.");
        var exampleText = await exampleDocument.GetTextAsync(ct).ConfigureAwait(false);

        var exampleSpan = BuildSelectionSpan(
            exampleText, exampleStartLine, exampleStartColumn, exampleEndLine, exampleEndColumn);

        var exampleExpression = FindContainedExpression(exampleRoot, exampleSpan);
        var exampleType = exampleExpression.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Example expression must be inside a type declaration.");

        // Derive the helper signature from free variables (locals + parameters) referenced by
        // the example expression. Each free variable becomes an ordered parameter; the expression's
        // inferred type becomes the helper's return type. Order-stable: first appearance wins.
        var exampleFreeVars = CollectFreeVariables(exampleExpression, exampleModel);
        var exampleTypeInfo = exampleModel.GetTypeInfo(exampleExpression, ct);
        var returnType = exampleTypeInfo.ConvertedType ?? exampleTypeInfo.Type
            ?? throw new InvalidOperationException(
                "Could not infer the expression's return type. The example span must resolve to a typed expression.");
        if (returnType.SpecialType == SpecialType.System_Void)
            throw new InvalidOperationException(
                "Cannot extract a void expression to a helper. Select a typed sub-expression instead.");

        // Scan candidate documents for structurally-identical expressions. Scope is the example's
        // containing type unless allowCrossFile=true, in which case we scan the whole project.
        var scope = allowCrossFile
            ? exampleDocument.Project.Documents
            : new[] { exampleDocument };
        var hits = new List<ExpressionHit>();
        foreach (var doc in scope)
        {
            ct.ThrowIfCancellationRequested();
            var docRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (docRoot is null) continue;
            var docModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (docModel is null) continue;

            foreach (var candidate in docRoot.DescendantNodes().OfType<ExpressionSyntax>())
            {
                if (!SyntaxFactory.AreEquivalent(exampleExpression, candidate, topLevel: false))
                    continue;

                // Apply containing-type guard when allowCrossFile=false. Different containing
                // types would force the helper to sit across a type boundary that the plan's
                // guard explicitly forbids.
                var candidateType = candidate.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (candidateType is null) continue;
                if (!allowCrossFile && candidateType != exampleType) continue;

                // Semantic-type guard: every free variable must have the same ITypeSymbol as the
                // example. Matches by position (structural equivalence aligns them 1:1).
                var candidateFreeVars = CollectFreeVariables(candidate, docModel);
                if (candidateFreeVars.Count != exampleFreeVars.Count)
                    throw new InvalidOperationException(
                        $"Structurally-identical match in '{doc.FilePath ?? doc.Name}' has {candidateFreeVars.Count} free variables; " +
                        $"the example has {exampleFreeVars.Count}. Cannot extract — the capture sets differ.");
                for (var i = 0; i < candidateFreeVars.Count; i++)
                {
                    var a = exampleFreeVars[i].Type;
                    var b = candidateFreeVars[i].Type;
                    if (!SymbolEqualityComparer.Default.Equals(a, b))
                        throw new InvalidOperationException(
                            $"Free-variable semantic-type mismatch at match in '{doc.FilePath ?? doc.Name}': " +
                            $"example variable '{exampleFreeVars[i].Name}' is '{a?.ToDisplayString() ?? "<null>"}' but " +
                            $"candidate variable '{candidateFreeVars[i].Name}' is '{b?.ToDisplayString() ?? "<null>"}'. " +
                            "Cannot synthesize a shared helper with different parameter types.");
                }

                hits.Add(new ExpressionHit(doc, candidate, candidateFreeVars, candidateType));
            }
        }

        if (hits.Count == 0)
            throw new InvalidOperationException(
                "No structurally-identical expressions found — even the example span did not match. " +
                "Verify the span covers a complete expression.");
        if (hits.Count < 2)
            throw new InvalidOperationException(
                "Only one occurrence of the expression was found. Use `extract_method_preview` for a single-site extraction; " +
                "`extract_shared_expression_to_helper_preview` is intended for 2+ call sites.");

        // Build the helper method. Place it on the example's containing type; every hit gets
        // rewritten to call it via {helperName}(arg1, arg2, ...). When allowCrossFile is true
        // and hits reside outside the example type, those call sites reach the helper via the
        // example-type's fully-qualified name.
        var helperTypeName = exampleType.Identifier.Text;
        var helperNamespace = exampleType.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();
        var helperFullyQualifiedType = string.IsNullOrEmpty(helperNamespace)
            ? helperTypeName
            : $"{helperNamespace}.{helperTypeName}";

        var helperMethod = BuildHelperMethod(
            helperName, accessibilityToken, returnType, exampleFreeVars, exampleExpression);

        // Rewrite hits grouped by document. Each document's edit is independent; the helper
        // insertion happens on the example doc in a second pass.
        var hitsByDocument = hits.GroupBy(h => h.Document.Id).ToList();
        var accumulator = solution;
        var fileChanges = new List<FileChangeDto>();

        foreach (var group in hitsByDocument)
        {
            ct.ThrowIfCancellationRequested();
            var doc = group.First().Document;
            var docRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Could not read syntax root for '{doc.FilePath ?? doc.Name}'.");
            var oldText = docRoot.ToFullString();

            var rewrites = group.ToDictionary(
                h => (SyntaxNode)h.Expression,
                h => BuildInvocation(
                    helperName,
                    h.FreeVariables,
                    allowCrossFile && h.ContainingType != exampleType ? helperFullyQualifiedType : null));

            var newDocRoot = docRoot.ReplaceNodes(
                rewrites.Keys,
                (original, _) => rewrites[original]
                    .WithTriviaFrom(original)
                    .WithAdditionalAnnotations(Formatter.Annotation));

            if (doc.Id == exampleDocument.Id)
            {
                newDocRoot = InsertHelperIntoType(newDocRoot, exampleType, helperMethod);
            }

            // Route the rewritten root through `Formatter.FormatAsync` so the synthesized helper
            // and the Formatter.Annotation-tagged invocation sites pick up editorconfig-driven
            // indentation and spacing. Without this, the raw SyntaxFactory output emits
            // `privatestaticstringHelper(stringarg)` (no whitespace between modifiers/tokens)
            // and the synthesized helper fails CS1520 at apply time.
            var documentWithEdit = doc.WithSyntaxRoot(newDocRoot);
            var formattedDocument = await Formatter.FormatAsync(
                documentWithEdit,
                Formatter.Annotation,
                options: null,
                cancellationToken: ct).ConfigureAwait(false);
            var formattedRoot = await formattedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Formatter produced no syntax root for '{doc.FilePath ?? doc.Name}'.");

            var newText = formattedRoot.ToFullString();
            accumulator = accumulator.WithDocumentSyntaxRoot(doc.Id, formattedRoot);

            var docPath = doc.FilePath ?? doc.Name;
            fileChanges.Add(new FileChangeDto(
                FilePath: docPath,
                UnifiedDiff: DiffGenerator.GenerateUnifiedDiff(oldText, newText, docPath)));
        }

        // If the example doc wasn't among the rewrite groups (can't actually happen since we
        // always include the example hit — but kept as defense-in-depth), still insert the
        // helper so the diff is legal.
        if (!hitsByDocument.Any(g => g.First().Document.Id == exampleDocument.Id))
        {
            var docRoot = await exampleDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
                ?? throw new InvalidOperationException("Could not re-read example document root.");
            var oldText = docRoot.ToFullString();
            var newDocRoot = InsertHelperIntoType(docRoot, exampleType, helperMethod);
            var documentWithEdit = exampleDocument.WithSyntaxRoot(newDocRoot);
            var formattedDocument = await Formatter.FormatAsync(
                documentWithEdit,
                Formatter.Annotation,
                options: null,
                cancellationToken: ct).ConfigureAwait(false);
            var formattedRoot = await formattedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Formatter produced no syntax root for the example document.");
            var newText = formattedRoot.ToFullString();
            accumulator = accumulator.WithDocumentSyntaxRoot(exampleDocument.Id, formattedRoot);
            var docPath = exampleDocument.FilePath ?? exampleDocument.Name;
            fileChanges.Add(new FileChangeDto(
                FilePath: docPath,
                UnifiedDiff: DiffGenerator.GenerateUnifiedDiff(oldText, newText, docPath)));
        }

        var description =
            $"Extract shared expression into helper '{helperName}' and rewrite {hits.Count} site(s) across {fileChanges.Count} file(s)";
        var token = _previewStore.Store(
            workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogDebug("Extract shared expression preview: {Description}", description);

        return new RefactoringPreviewDto(token, description, fileChanges, null);
    }

    private static SyntaxToken ParseAccessibility(string accessibility)
    {
        if (string.IsNullOrWhiteSpace(accessibility))
            return SyntaxFactory.Token(SyntaxKind.PrivateKeyword);

        return accessibility.Trim().ToLowerInvariant() switch
        {
            "private" => SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
            "internal" => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
            "public" => SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            _ => throw new ArgumentException(
                $"Unsupported accessibility '{accessibility}'. Use private, internal, or public.",
                nameof(accessibility))
        };
    }

    private static ExpressionSyntax FindContainedExpression(
        CompilationUnitSyntax root, Microsoft.CodeAnalysis.Text.TextSpan selectionSpan)
    {
        // Prefer the deepest expression fully contained by the selection span; fall back to the
        // innermost expression touching the span. The caller supplies an example span that
        // should bracket a complete sub-expression.
        ExpressionSyntax? best = null;
        foreach (var expr in root.DescendantNodes().OfType<ExpressionSyntax>())
        {
            if (!selectionSpan.Contains(expr.Span)) continue;
            if (best is null || expr.Span.Length > best.Span.Length) best = expr;
        }

        if (best is not null) return best;

        // Fall back: find innermost expression overlapping the span.
        var startNode = root.FindNode(selectionSpan, getInnermostNodeForTie: true);
        var innermost = startNode.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault();
        return innermost
            ?? throw new InvalidOperationException(
                "Example span does not resolve to any C# expression. Select a complete sub-expression.");
    }

    private static List<FreeVariable> CollectFreeVariables(ExpressionSyntax expression, SemanticModel model)
    {
        // A "free variable" is a local/parameter symbol that is REFERENCED by the expression but
        // is DECLARED OUTSIDE the expression's own lexical boundary. Order-preserving by the
        // syntactic position of the first reference — matches how invocation arguments will be
        // emitted at each rewrite site.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<FreeVariable>();
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;
            if (symbol is not ILocalSymbol and not IParameterSymbol) continue;

            // `this` parameter is implicit; not a free variable the caller should supply.
            if (symbol is IParameterSymbol p && p.IsThis) continue;

            var name = symbol.Name;
            if (!seen.Add(name)) continue;

            var type = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            };
            if (type is null) continue;
            list.Add(new FreeVariable(name, type));
        }
        return list;
    }

    private static MethodDeclarationSyntax BuildHelperMethod(
        string helperName,
        SyntaxToken accessibility,
        ITypeSymbol returnType,
        IReadOnlyList<FreeVariable> freeVariables,
        ExpressionSyntax expression)
    {
        var returnTypeSyntax = SyntaxFactory.ParseTypeName(
            returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        var parameterList = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(
                freeVariables.Select(v =>
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(v.Name))
                        .WithType(SyntaxFactory.ParseTypeName(
                            v.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))))));
        // Strip trivia from the expression clone: the helper's body sits on its own line and
        // should not inherit leading/trailing comments or whitespace from its first appearance.
        var bodyExpression = expression.WithoutTrivia();
        var body = SyntaxFactory.Block(
            SyntaxFactory.List<StatementSyntax>(new[]
            {
                (StatementSyntax)SyntaxFactory.ReturnStatement(bodyExpression)
                    .WithAdditionalAnnotations(Formatter.Annotation)
            }));

        return SyntaxFactory.MethodDeclaration(returnTypeSyntax, SyntaxFactory.Identifier(helperName))
            .WithModifiers(SyntaxFactory.TokenList(
                accessibility,
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(parameterList)
            .WithBody(body)
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static InvocationExpressionSyntax BuildInvocation(
        string helperName,
        IReadOnlyList<FreeVariable> freeVariables,
        string? fullyQualifiedTypePrefix)
    {
        var argumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(
                freeVariables.Select(v => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(v.Name)))));

        ExpressionSyntax target = string.IsNullOrEmpty(fullyQualifiedTypePrefix)
            ? SyntaxFactory.IdentifierName(helperName)
            : SyntaxFactory.ParseExpression($"{fullyQualifiedTypePrefix}.{helperName}");

        // Formatter.Annotation lets `Formatter.FormatAsync` re-flow spacing around commas,
        // parentheses, and (when fully-qualified) dotted member-access chains per editorconfig.
        return SyntaxFactory.InvocationExpression(target, argumentList)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static SyntaxNode InsertHelperIntoType(
        SyntaxNode root, TypeDeclarationSyntax exampleType, MethodDeclarationSyntax helper)
    {
        // Locate the type in the possibly-rewritten tree by its identifier+position — the tree
        // may already have been mutated (expression rewrites). Fall back to identifier-match
        // when the span shifted.
        var newType = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == exampleType.Identifier.Text
                                 && t.SpanStart == exampleType.SpanStart)
            ?? root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.Text == exampleType.Identifier.Text)
            ?? throw new InvalidOperationException(
                $"Could not re-locate type '{exampleType.Identifier.Text}' in the rewritten tree.");
        var updatedType = newType.AddMembers(helper);
        return root.ReplaceNode(newType, updatedType);
    }

    private sealed record FreeVariable(string Name, ITypeSymbol Type);

    private sealed record ExpressionHit(
        Document Document,
        ExpressionSyntax Expression,
        IReadOnlyList<FreeVariable> FreeVariables,
        TypeDeclarationSyntax ContainingType);
}
