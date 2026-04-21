using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 3 implementation. Supports <c>add</c>, <c>remove</c>, and <c>rename</c> ops by
/// rewriting both the method declaration and every callsite in one atomic preview.
/// Reordering parameters is not supported — callers that need it should stage a
/// remove + add pair via <c>symbol_refactor_preview</c>.
/// </summary>
public sealed class ChangeSignatureService : IChangeSignatureService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly IRefactoringService _refactoringService;

    public ChangeSignatureService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        IRefactoringService refactoringService)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _refactoringService = refactoringService;
    }

    public async Task<RefactoringPreviewDto> PreviewChangeSignatureAsync(
        string workspaceId, SymbolLocator locator, ChangeSignatureRequest request, CancellationToken ct)
    {
        locator.Validate();
        if (string.IsNullOrWhiteSpace(request.Op))
            throw new ArgumentException("ChangeSignatureRequest.Op is required.", nameof(request));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);

        // change-signature-parameter-span-hint-for-remove: accept a caret ON the parameter
        // itself as a shortcut. Pre-fix the service required the caret to point at the method
        // declaration, which forced agents with a parameter caret to translate a parameter
        // name into a 0-based Position or re-click at the method name. If the caret resolves
        // to a parameter symbol, promote to the containing method and splice the parameter's
        // 0-based index into the request when Position/Name are both unset. Explicit values
        // still win (caret-on-method with explicit Name is unchanged).
        if (symbol is IParameterSymbol parameter && parameter.ContainingSymbol is IMethodSymbol paramOwner)
        {
            symbol = paramOwner;
            if (request.Position is null && string.IsNullOrWhiteSpace(request.Name))
            {
                var parameterIndex = paramOwner.Parameters.IndexOf(parameter);
                if (parameterIndex >= 0)
                {
                    request = request with { Position = parameterIndex };
                }
            }
        }

        if (symbol is not IMethodSymbol method)
            throw new InvalidOperationException(
                $"change_signature_preview requires a method symbol; resolved {symbol?.Kind.ToString() ?? "null"} instead.");

        return request.Op.ToLowerInvariant() switch
        {
            "add" => await PreviewAddParameterAsync(workspaceId, solution, method, request, ct).ConfigureAwait(false),
            "remove" => await PreviewRemoveParameterAsync(workspaceId, solution, method, request, ct).ConfigureAwait(false),
            "rename" => await PreviewRenameParameterAsync(workspaceId, method, request, ct).ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"Unsupported op '{request.Op}'. Valid values: add, remove, rename. " +
                "Parameter reordering is not supported — stage a remove + add pair via symbol_refactor_preview " +
                "or fall back to preview_multi_file_edit.",
                nameof(request)),
        };
    }

    private async Task<RefactoringPreviewDto> PreviewAddParameterAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("op='add' requires Name.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ParameterType))
            throw new ArgumentException("op='add' requires ParameterType.", nameof(request));

        var insertPosition = request.Position ?? method.Parameters.Length;
        if (insertPosition < 0 || insertPosition > method.Parameters.Length)
            throw new ArgumentException(
                $"Position {insertPosition} is out of range (0..{method.Parameters.Length}).", nameof(request));

        // change-signature-preview-brace-concat-on-add-op:
        // Build the new parameter via SyntaxFactory.ParseParameter — the parser produces a
        // properly-tokenized ParameterSyntax (type + space + identifier + optional `= value`).
        // The previous implementation used SyntaxFactory.Parameter + ParseTypeName(type + " ")
        // which left the parameter without inter-token trivia, so the rendered preview ran
        // tokens together (e.g. `intx` instead of `int x`) and dropped any default value.
        var paramText = string.IsNullOrWhiteSpace(request.DefaultValue)
            ? $"{request.ParameterType} {request.Name}"
            : $"{request.ParameterType} {request.Name} = {request.DefaultValue}";
        var argText = request.DefaultValue ?? "default";

        var (accumulator, changes, callsiteUpdates) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: (existingList) =>
            {
                // Build a fully-parsed parameter list from text so commas + parameter
                // separators get correct trivia. Preserve the existing list's leading +
                // trailing trivia (the close-paren's trailing trivia carries the
                // whitespace before the method body's opening brace — losing it caused
                // the brace-concat symptom from the audit).
                return BuildParameterListFromTextChange(existingList, addParam: paramText, addAt: insertPosition, removeAt: null);
            },
            updateCallsite: (args, isPositional) =>
            {
                if (isPositional)
                {
                    var newArg = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(argText));
                    return args.Insert(insertPosition, newArg.WithLeadingTrivia(insertPosition == 0 ? default : SyntaxFactory.Space));
                }
                // Caller uses named args — splice by name so we don't break existing positions.
                var named = SyntaxFactory.Argument(
                    SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(request.Name!)),
                    refKindKeyword: default,
                    expression: SyntaxFactory.ParseExpression(argText));
                return args.Add(named.WithLeadingTrivia(args.Count == 0 ? default : SyntaxFactory.Space));
            },
            ct).ConfigureAwait(false);

        return BuildPreviewDto(workspaceId, accumulator, changes, callsiteUpdates,
            $"Add parameter '{request.Name}: {request.ParameterType}' at position {insertPosition} of {method.ToDisplayString()}");
    }

    private async Task<RefactoringPreviewDto> PreviewRemoveParameterAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        var index = ResolveParameterIndex(method, request);
        if (index < 0) throw new ArgumentException(
            $"op='remove' requires Position or Name to identify the parameter.", nameof(request));

        var (accumulator, changes, callsiteUpdates) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: existingList => BuildParameterListFromTextChange(existingList, addParam: null, addAt: null, removeAt: index),
            updateCallsite: (args, isPositional) =>
            {
                if (isPositional && index < args.Count)
                    return args.RemoveAt(index);
                var paramName = method.Parameters[index].Name;
                for (var i = 0; i < args.Count; i++)
                {
                    var nameColon = args[i].NameColon;
                    if (nameColon is not null && string.Equals(nameColon.Name.Identifier.ValueText, paramName, StringComparison.Ordinal))
                        return args.RemoveAt(i);
                }
                return args; // not present at this callsite
            },
            ct).ConfigureAwait(false);

        return BuildPreviewDto(workspaceId, accumulator, changes, callsiteUpdates,
            $"Remove parameter '{method.Parameters[index].Name}' (position {index}) from {method.ToDisplayString()}");
    }

    /// <summary>
    /// Builds a new ParameterList by serializing the existing parameters to text, applying
    /// add/remove edits, and reparsing through <see cref="SyntaxFactory.ParseParameterList"/>.
    /// This produces correct comma + space trivia between every parameter — the raw
    /// SeparatedSyntaxList API path loses these. Trivia on the enclosing list (and therefore
    /// on the close-paren) is copied from the original list so the method-body brace stays
    /// on its own line.
    /// </summary>
    private static ParameterListSyntax BuildParameterListFromTextChange(
        ParameterListSyntax existingList,
        string? addParam,
        int? addAt,
        int? removeAt)
    {
        var paramTexts = new List<string>(existingList.Parameters.Count + 1);
        for (var i = 0; i < existingList.Parameters.Count; i++)
        {
            if (removeAt is int r && r == i) continue;
            if (addAt is int a && a == i && addParam is not null) paramTexts.Add(addParam);
            paramTexts.Add(existingList.Parameters[i].ToString());
        }
        if (addAt is int aTail && aTail >= existingList.Parameters.Count && addParam is not null)
        {
            paramTexts.Add(addParam);
        }

        var newListText = "(" + string.Join(", ", paramTexts) + ")";
        var newList = SyntaxFactory.ParseParameterList(newListText);
        return newList.WithTriviaFrom(existingList);
    }

    /// <summary>
    /// Rename of a method parameter delegates to the existing rename engine on the parameter
    /// symbol — that engine already handles named-argument call sites correctly.
    /// </summary>
    private async Task<RefactoringPreviewDto> PreviewRenameParameterAsync(
        string workspaceId, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("op='rename' requires Name (current parameter name).", nameof(request));
        if (string.IsNullOrWhiteSpace(request.NewName))
            throw new ArgumentException("op='rename' requires NewName.", nameof(request));

        var paramSymbol = method.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, request.Name, StringComparison.Ordinal));
        if (paramSymbol is null)
            throw new ArgumentException(
                $"Parameter '{request.Name}' not found on method {method.ToDisplayString()}.", nameof(request));

        var loc = paramSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null) throw new InvalidOperationException("Parameter has no source location to rename from.");
        var lineSpan = loc.GetLineSpan();
        var renameLocator = SymbolLocator.BySource(loc.SourceTree!.FilePath, lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Character + 1);
        return await _refactoringService.PreviewRenameAsync(workspaceId, renameLocator, request.NewName, ct).ConfigureAwait(false);
    }

    private static int ResolveParameterIndex(IMethodSymbol method, ChangeSignatureRequest request)
    {
        if (request.Position is int p && p >= 0 && p < method.Parameters.Length) return p;
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            for (var i = 0; i < method.Parameters.Length; i++)
            {
                if (string.Equals(method.Parameters[i].Name, request.Name, StringComparison.Ordinal)) return i;
            }
        }
        return -1;
    }

    private async Task<(Solution Accumulator, List<FileChangeDto> Changes, List<CallsiteUpdateDto> CallsiteUpdates)> ApplyAddRemoveAsync(
        Solution solution,
        IMethodSymbol method,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<SeparatedSyntaxList<ArgumentSyntax>, bool, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        // Track original (pre-mutation) text per document so the final unified diff is computed
        // from original-to-final — not from whatever intermediate state the previous iteration
        // left behind. Earlier implementations recomputed a per-iteration "before/after" which
        // made multi-callsite files surface only the last callsite change, and mutated-state
        // reads as "before" on subsequent iterations.
        var originalTexts = new Dictionary<DocumentId, string>();

        // change-signature-preview-callsite-summary: per-file callsite counts so callers
        // can audit total reach (especially through interface dispatch) without parsing
        // every diff. Keyed by absolute file path; counts only invocation rewrites
        // (declaration changes do not contribute).
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // change-signature-preview-callsite-summary (firewall-analyzer 2026-04-15):
        // Build the related-symbol set ONCE — used for declaration rewrites in phase 1
        // AND for caller enumeration in phase 2. SymbolFinder.FindCallersAsync(method)
        // returns callers that invoke through THIS exact symbol. For an interface
        // method, that misses callers invoking through implementing classes; for an
        // implementer, it misses callers invoking through the interface. Same shape for
        // declarations — when changing the signature on an interface method, every
        // implementer must also update its declaration to keep compiling.
        var symbolsToScan = await CollectRelatedSymbolsAsync(method, solution, ct).ConfigureAwait(false);

        // Phase 1: update every related symbol's declaration(s). Includes the interface method,
        // every implementing class, every override, and (for partial methods) both definition +
        // implementation. Without this pass, changing an interface method would break every
        // implementer at compile time.
        var accumulator = await RewriteRelatedDeclarationsAsync(
            solution, symbolsToScan, updateDeclaration, originalTexts, ct).ConfigureAwait(false);

        // Phase 2: collect all unique caller locations across the related-symbol set. Spans are
        // captured against the ORIGINAL solution; accumulator's per-iteration mutations would
        // shift the indices and break later span lookups.
        var callerLocations = await CollectCallerSpansAsync(
            solution, symbolsToScan, ct).ConfigureAwait(false);

        // Phase 3: rewrite per document. Sort spans descending so each ReplaceNode preserves
        // the validity of earlier (lower-offset) spans within the same document. Capture the
        // original text on first visit per document so the end-of-pass diff is against the
        // untouched baseline.
        accumulator = await RewriteCallerArgumentsAsync(
            accumulator, solution, callerLocations, updateCallsite, originalTexts, perFileCallsites, ct).ConfigureAwait(false);

        // Phase 4: emit ONE unified diff per touched file, original → final. DiffGenerator
        // applies a 16 KB cap with a truncation marker so the preview never blows past MCP
        // payload budgets.
        var changes = await BuildFileChangesAsync(accumulator, originalTexts, ct).ConfigureAwait(false);

        var callsiteUpdates = perFileCallsites
            .Select(kvp => new CallsiteUpdateDto(kvp.Key, kvp.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal)
            .ToList();

        return (accumulator, changes, callsiteUpdates);
    }

    /// <summary>
    /// Return the original (untouched-solution) text for <paramref name="docId"/>, caching the
    /// read in <paramref name="originalTexts"/>. Subsequent reads are served from the dict so
    /// the end-of-pass diff is computed against a stable baseline even after many mutations.
    /// </summary>
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

    /// <summary>
    /// Phase 1 helper: walk every <paramref name="symbolsToScan"/> entry's declaring syntax
    /// references, find the <see cref="BaseMethodDeclarationSyntax"/> for each, and apply
    /// <paramref name="updateDeclaration"/> to its parameter list. De-dupes per
    /// (document, parameter-list-span) so a partial method visited from two sides rewrites only
    /// once. Captures each touched document's original text into
    /// <paramref name="originalTexts"/>.
    /// </summary>
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

                // Re-find the parameter list inside the (possibly already-modified) root —
                // the symbol's declaration position is from the original solution; if a
                // sibling iteration already mutated the document, the original span may no
                // longer be valid.
                var currentMds = oldRoot.FindNode(mds.Span).FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
                if (currentMds is null) continue;
                var newParamList = updateDeclaration(currentMds.ParameterList);
                var newRoot = oldRoot.ReplaceNode(currentMds.ParameterList, newParamList);
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));
            }
        }
        return accumulator;
    }

    /// <summary>
    /// Phase 2 helper: for every related symbol, call
    /// <see cref="SymbolFinder.FindCallersAsync(ISymbol, Solution, CancellationToken)"/> against
    /// the ORIGINAL solution and collect each caller's source location. Returns a per-document
    /// list of unique source spans; the caller rewrites in phase 3 sort descending and replace
    /// so that earlier spans stay valid under intra-document mutation.
    /// </summary>
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
                    if (!spans.Contains(location.SourceSpan)) spans.Add(location.SourceSpan);
                }
            }
        }
        return callerLocations;
    }

    /// <summary>
    /// Phase 3 helper: rewrite each caller's argument list via <paramref name="updateCallsite"/>.
    /// Per-document loop sorts spans descending so each <c>WithDocumentText</c> preserves the
    /// validity of earlier (lower-offset) spans within the same document. Captures the original
    /// text on first visit per document so the end-of-pass diff is against the untouched baseline,
    /// and increments <paramref name="perFileCallsites"/> whenever an invocation actually changes.
    /// </summary>
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

    /// <summary>
    /// Phase 4 helper: emit ONE unified diff per touched file, original → final text. Documents
    /// whose final text matches the captured original (e.g. a caller span that yielded
    /// <c>newArgs.Equals(args)</c>) are skipped so the preview only lists documents that actually
    /// changed.
    /// </summary>
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

    /// <summary>
    /// Returns the union of <paramref name="method"/> with every related symbol whose
    /// callers should be rewritten when the signature changes:
    /// <list type="bullet">
    ///   <item><description>The method itself.</description></item>
    ///   <item><description>If <paramref name="method"/> is an interface member: every implementing method.</description></item>
    ///   <item><description>If <paramref name="method"/> is an implementing method: the interface members it implements + every other implementer.</description></item>
    ///   <item><description>If <paramref name="method"/> is virtual/abstract: all overrides + the base virtual.</description></item>
    /// </list>
    /// SymbolFinder.FindCallersAsync against any single symbol misses callers that invoke
    /// through a related symbol (interface vs concrete dispatch); the union ensures the
    /// preview enumerates every file the apply will touch.
    /// </summary>
    private static async Task<IReadOnlyList<IMethodSymbol>> CollectRelatedSymbolsAsync(
        IMethodSymbol method, Solution solution, CancellationToken ct)
    {
        var set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        set.Add(method);

        // Interface members: include every implementer, scoped to the solution.
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
            // Implementing method: walk back to interface members, then forward to siblings.
            foreach (var iface in method.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var concrete = method.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                    if (SymbolEqualityComparer.Default.Equals(concrete, method))
                    {
                        set.Add(ifaceMember);
                        var siblingImpls = await SymbolFinder.FindImplementationsAsync(ifaceMember, solution, cancellationToken: ct).ConfigureAwait(false);
                        foreach (var s in siblingImpls)
                        {
                            if (s is IMethodSymbol sm) set.Add(sm);
                        }
                    }
                }
            }
        }

        // Virtual / abstract: union with overrides + base.
        if (method.IsVirtual || method.IsAbstract || method.IsOverride)
        {
            var overrides = await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var o in overrides)
            {
                if (o is IMethodSymbol om) set.Add(om);
            }
            for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
            {
                set.Add(current);
            }
        }

        return [.. set];
    }

    private RefactoringPreviewDto BuildPreviewDto(
        string workspaceId,
        Solution accumulator,
        List<FileChangeDto> changes,
        List<CallsiteUpdateDto> callsiteUpdates,
        string description)
    {
        if (changes.Count == 0)
            throw new InvalidOperationException("change_signature_preview produced no changes — verify the symbol actually has callers.");
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            Warnings: null,
            CallsiteUpdates: callsiteUpdates.Count == 0 ? null : callsiteUpdates);
    }
}
