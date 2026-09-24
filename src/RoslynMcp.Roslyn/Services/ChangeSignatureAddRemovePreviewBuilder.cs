using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class ChangeSignatureAddRemovePreviewBuilder
{
    public static async Task<(Solution Accumulator, List<FileChangeDto> Changes, List<CallsiteUpdateDto> CallsiteUpdates)> BuildAsync(
        Solution solution,
        IMethodSymbol method,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<SeparatedSyntaxList<ArgumentSyntax>, bool, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        var originalTexts = new Dictionary<DocumentId, string>();
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Every declaration and caller span is collected against the ORIGINAL solution and
        // grouped per document, then each document is rewritten exactly once from its original
        // root. Re-resolving original trees/spans against an already-edited document silently
        // skipped every same-document declaration or caller after the first edit.
        var symbolsToScan = await CollectRelatedSymbolsAsync(method, solution, ct).ConfigureAwait(false);
        var targets = new Dictionary<DocumentId, DocumentRewriteTargets>();
        await CollectDeclarationSpansAsync(solution, symbolsToScan, targets, ct).ConfigureAwait(false);
        await CollectCallerSpansAsync(solution, symbolsToScan, targets, ct).ConfigureAwait(false);

        var accumulator = await RewriteDocumentsAsync(
            solution, targets, updateDeclaration, updateCallsite, originalTexts, perFileCallsites, ct).ConfigureAwait(false);

        var changes = await BuildFileChangesAsync(accumulator, originalTexts, ct).ConfigureAwait(false);
        var callsiteUpdates = perFileCallsites
            .Select(kvp => new CallsiteUpdateDto(kvp.Key, kvp.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal)
            .ToList();

        return (accumulator, changes, callsiteUpdates);
    }

    private sealed class DocumentRewriteTargets
    {
        public HashSet<TextSpan> DeclarationSpans { get; } = [];

        public HashSet<TextSpan> CallerSpans { get; } = [];
    }

    private static DocumentRewriteTargets GetTargets(Dictionary<DocumentId, DocumentRewriteTargets> targets, DocumentId docId)
    {
        if (!targets.TryGetValue(docId, out var docTargets))
        {
            docTargets = new DocumentRewriteTargets();
            targets[docId] = docTargets;
        }

        return docTargets;
    }

    private static async Task CollectDeclarationSpansAsync(
        Solution solution,
        IReadOnlyList<IMethodSymbol> symbolsToScan,
        Dictionary<DocumentId, DocumentRewriteTargets> targets,
        CancellationToken ct)
    {
        foreach (var sym in symbolsToScan)
        {
            foreach (var declRef in sym.DeclaringSyntaxReferences)
            {
                var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
                if (node is not BaseMethodDeclarationSyntax mds) continue;
                var doc = solution.GetDocument(node.SyntaxTree);
                if (doc is null) continue;

                GetTargets(targets, doc.Id).DeclarationSpans.Add(mds.Span);
            }
        }
    }

    private static async Task CollectCallerSpansAsync(
        Solution solution,
        IReadOnlyList<IMethodSymbol> symbolsToScan,
        Dictionary<DocumentId, DocumentRewriteTargets> targets,
        CancellationToken ct)
    {
        foreach (var sym in symbolsToScan)
        {
            var callers = await SymbolFinder.FindCallersAsync(sym, solution, ct).ConfigureAwait(false);
            foreach (var caller in callers)
            {
                foreach (var location in caller.Locations)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!location.IsInSource) continue;

                    var originalDoc = solution.GetDocument(location.SourceTree);
                    if (originalDoc is null) continue;

                    GetTargets(targets, originalDoc.Id).CallerSpans.Add(location.SourceSpan);
                }
            }
        }
    }

    private static async Task<Solution> RewriteDocumentsAsync(
        Solution solution,
        Dictionary<DocumentId, DocumentRewriteTargets> targets,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<SeparatedSyntaxList<ArgumentSyntax>, bool, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        Dictionary<DocumentId, string> originalTexts,
        Dictionary<string, int> perFileCallsites,
        CancellationToken ct)
    {
        var accumulator = solution;
        foreach (var (docId, docTargets) in targets)
        {
            ct.ThrowIfCancellationRequested();
            var doc = solution.GetDocument(docId);
            if (doc is null) continue;
            var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (oldRoot is null) continue;

            // All spans are original-solution spans, so they resolve against the original root.
            var nodesToRewrite = new HashSet<SyntaxNode>();
            foreach (var span in docTargets.DeclarationSpans)
            {
                var declaration = oldRoot.FindNode(span).FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
                if (declaration is not null) nodesToRewrite.Add(declaration.ParameterList);
            }

            foreach (var span in docTargets.CallerSpans)
            {
                var invocation = oldRoot.FindNode(span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is not null) nodesToRewrite.Add(invocation);
            }

            if (nodesToRewrite.Count == 0) continue;

            await CaptureOriginalTextAsync(originalTexts, solution, docId, ct).ConfigureAwait(false);

            var callsiteCount = 0;
            var newRoot = oldRoot.ReplaceNodes(nodesToRewrite, (_, rewritten) =>
            {
                switch (rewritten)
                {
                    case ParameterListSyntax parameterList:
                        return updateDeclaration(parameterList);
                    case InvocationExpressionSyntax invocation:
                        var args = invocation.ArgumentList.Arguments;
                        var isPositional = args.All(a => a.NameColon is null);
                        var newArgs = updateCallsite(args, isPositional);
                        if (newArgs.Equals(args)) return invocation;
                        callsiteCount++;
                        return invocation.WithArgumentList(invocation.ArgumentList.WithArguments(newArgs));
                    default:
                        return rewritten;
                }
            });

            accumulator = accumulator.WithDocumentText(docId, SourceText.From(newRoot.ToFullString()));

            if (callsiteCount > 0)
            {
                var filePath = doc.FilePath ?? doc.Name;
                perFileCallsites[filePath] = perFileCallsites.TryGetValue(filePath, out var count)
                    ? count + callsiteCount
                    : callsiteCount;
            }
        }

        return accumulator;
    }

    private static async Task<string> CaptureOriginalTextAsync(
        Dictionary<DocumentId, string> originalTexts,
        Solution solution,
        DocumentId docId,
        CancellationToken ct)
    {
        if (originalTexts.TryGetValue(docId, out var cached)) return cached;
        var doc = solution.GetDocument(docId);
        if (doc is null) return string.Empty;
        var text = (await doc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
        originalTexts[docId] = text;
        return text;
    }

    private static async Task<List<FileChangeDto>> BuildFileChangesAsync(
        Solution accumulator,
        Dictionary<DocumentId, string> originalTexts,
        CancellationToken ct)
    {
        var changes = new List<FileChangeDto>(originalTexts.Count);
        foreach (var (docId, originalText) in originalTexts)
        {
            var finalDoc = accumulator.GetDocument(docId);
            if (finalDoc is null) continue;
            var finalText = (await finalDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (string.Equals(finalText, originalText, StringComparison.Ordinal)) continue;
            var filePath = finalDoc.FilePath ?? finalDoc.Name;
            changes.Add(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(originalText, finalText, filePath)));
        }

        return changes;
    }

    private static async Task<IReadOnlyList<IMethodSymbol>> CollectRelatedSymbolsAsync(
        IMethodSymbol method,
        Solution solution,
        CancellationToken ct)
    {
        var set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { method };

        if (method.ContainingType is { TypeKind: TypeKind.Interface })
        {
            var impls = await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var impl in impls)
            {
                if (impl is IMethodSymbol implMethod) set.Add(implMethod);
            }
        }
        else
        {
            foreach (var ifaceMember in SymbolServiceHelpers.GetImplementedInterfaceMembers(method))
            {
                set.Add(ifaceMember);
                var siblingImpls = await SymbolFinder.FindImplementationsAsync(
                    ifaceMember,
                    solution,
                    cancellationToken: ct).ConfigureAwait(false);
                foreach (var sibling in siblingImpls)
                {
                    if (sibling is IMethodSymbol siblingMethod) set.Add(siblingMethod);
                }
            }
        }

        if (method.IsVirtual || method.IsAbstract || method.IsOverride)
        {
            var overrides = await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var @override in overrides)
            {
                if (@override is IMethodSymbol overrideMethod) set.Add(overrideMethod);
            }

            for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
            {
                set.Add(current);
            }
        }

        return [.. set];
    }
}
