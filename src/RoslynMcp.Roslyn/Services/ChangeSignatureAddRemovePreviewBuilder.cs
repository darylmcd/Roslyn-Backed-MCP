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

        var symbolsToScan = await CollectRelatedSymbolsAsync(method, solution, ct).ConfigureAwait(false);
        var accumulator = await RewriteRelatedDeclarationsAsync(
            solution, symbolsToScan, updateDeclaration, originalTexts, ct).ConfigureAwait(false);

        var callerLocations = await CollectCallerSpansAsync(solution, symbolsToScan, ct).ConfigureAwait(false);
        accumulator = await RewriteCallerArgumentsAsync(
            accumulator, solution, callerLocations, updateCallsite, originalTexts, perFileCallsites, ct).ConfigureAwait(false);

        var changes = await BuildFileChangesAsync(accumulator, originalTexts, ct).ConfigureAwait(false);
        var callsiteUpdates = perFileCallsites
            .Select(kvp => new CallsiteUpdateDto(kvp.Key, kvp.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal)
            .ToList();

        return (accumulator, changes, callsiteUpdates);
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

    private static async Task<Solution> RewriteRelatedDeclarationsAsync(
        Solution solution,
        IReadOnlyList<IMethodSymbol> symbolsToScan,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Dictionary<DocumentId, string> originalTexts,
        CancellationToken ct)
    {
        var accumulator = solution;
        var visitedDeclarationSpans = new HashSet<(DocumentId DocId, TextSpan Span)>();
        foreach (var sym in symbolsToScan)
        {
            foreach (var declRef in sym.DeclaringSyntaxReferences)
            {
                var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
                if (node is not BaseMethodDeclarationSyntax mds) continue;
                var doc = accumulator.GetDocument(node.SyntaxTree);
                if (doc is null) continue;
                if (!visitedDeclarationSpans.Add((doc.Id, mds.ParameterList.Span))) continue;

                await CaptureOriginalTextAsync(originalTexts, solution, doc.Id, ct).ConfigureAwait(false);
                var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (oldRoot is null) continue;

                var currentMds = oldRoot.FindNode(mds.Span).FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
                if (currentMds is null) continue;
                var newParamList = updateDeclaration(currentMds.ParameterList);
                var newRoot = oldRoot.ReplaceNode(currentMds.ParameterList, newParamList);
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));
            }
        }

        return accumulator;
    }

    private static async Task<Dictionary<DocumentId, List<TextSpan>>> CollectCallerSpansAsync(
        Solution solution,
        IReadOnlyList<IMethodSymbol> symbolsToScan,
        CancellationToken ct)
    {
        var callerLocations = new Dictionary<DocumentId, List<TextSpan>>();
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

                    if (!callerLocations.TryGetValue(originalDoc.Id, out var spans))
                    {
                        spans = [];
                        callerLocations[originalDoc.Id] = spans;
                    }

                    if (!spans.Contains(location.SourceSpan))
                    {
                        spans.Add(location.SourceSpan);
                    }
                }
            }
        }

        return callerLocations;
    }

    private static async Task<Solution> RewriteCallerArgumentsAsync(
        Solution accumulator,
        Solution solution,
        Dictionary<DocumentId, List<TextSpan>> callerLocations,
        Func<SeparatedSyntaxList<ArgumentSyntax>, bool, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        Dictionary<DocumentId, string> originalTexts,
        Dictionary<string, int> perFileCallsites,
        CancellationToken ct)
    {
        foreach (var (docId, spans) in callerLocations)
        {
            ct.ThrowIfCancellationRequested();
            var doc = accumulator.GetDocument(docId);
            if (doc is null) continue;

            await CaptureOriginalTextAsync(originalTexts, solution, doc.Id, ct).ConfigureAwait(false);

            spans.Sort((a, b) => b.Start.CompareTo(a.Start));
            foreach (var span in spans)
            {
                ct.ThrowIfCancellationRequested();
                doc = accumulator.GetDocument(docId);
                if (doc is null) break;
                var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (oldRoot is null) break;

                var node = oldRoot.FindNode(span);
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is null) continue;

                var args = invocation.ArgumentList.Arguments;
                var isPositional = args.All(a => a.NameColon is null);
                var newArgs = updateCallsite(args, isPositional);
                if (newArgs.Equals(args)) continue;
                var newInvocation = invocation.WithArgumentList(invocation.ArgumentList.WithArguments(newArgs));
                var newRoot = oldRoot.ReplaceNode(invocation, newInvocation);
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));

                var filePath = doc.FilePath ?? doc.Name;
                perFileCallsites[filePath] = perFileCallsites.TryGetValue(filePath, out var count) ? count + 1 : 1;
            }
        }

        return accumulator;
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
            foreach (var iface in method.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var concrete = method.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                    if (SymbolEqualityComparer.Default.Equals(concrete, method))
                    {
                        set.Add(ifaceMember);
                        var siblingImpls = await SymbolFinder.FindImplementationsAsync(ifaceMember, solution, cancellationToken: ct).ConfigureAwait(false);
                        foreach (var sibling in siblingImpls)
                        {
                            if (sibling is IMethodSymbol siblingMethod) set.Add(siblingMethod);
                        }
                    }
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
