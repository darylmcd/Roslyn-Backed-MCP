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

        var (accumulator, changes) = await ApplyAddRemoveAsync(
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

        return BuildPreviewDto(workspaceId, accumulator, changes,
            $"Add parameter '{request.Name}: {request.ParameterType}' at position {insertPosition} of {method.ToDisplayString()}");
    }

    private async Task<RefactoringPreviewDto> PreviewRemoveParameterAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        var index = ResolveParameterIndex(method, request);
        if (index < 0) throw new ArgumentException(
            $"op='remove' requires Position or Name to identify the parameter.", nameof(request));

        var (accumulator, changes) = await ApplyAddRemoveAsync(
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

        return BuildPreviewDto(workspaceId, accumulator, changes,
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

    private async Task<(Solution Accumulator, List<FileChangeDto> Changes)> ApplyAddRemoveAsync(
        Solution solution,
        IMethodSymbol method,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<SeparatedSyntaxList<ArgumentSyntax>, bool, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        var accumulator = solution;

        // Track original (pre-mutation) text per document so the final unified diff is computed
        // from original-to-final — not from whatever intermediate state the previous iteration
        // left behind. Earlier implementations recomputed a per-iteration "before/after" which
        // made multi-callsite files surface only the last callsite change, and mutated-state
        // reads as "before" on subsequent iterations.
        var originalTexts = new Dictionary<DocumentId, string>();

        async Task<string> CaptureOriginalAsync(DocumentId docId)
        {
            if (originalTexts.TryGetValue(docId, out var cached)) return cached;
            var doc = solution.GetDocument(docId);
            if (doc is null) return string.Empty;
            var text = (await doc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            originalTexts[docId] = text;
            return text;
        }

        // 1. Update the declaration(s). Most methods have one source declaration; partial methods
        //    can have two (definition + implementation) — both need updating.
        foreach (var declRef in method.DeclaringSyntaxReferences)
        {
            var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (node is not BaseMethodDeclarationSyntax mds) continue;
            var newParamList = updateDeclaration(mds.ParameterList);
            var doc = accumulator.GetDocument(node.SyntaxTree);
            if (doc is null) continue;
            await CaptureOriginalAsync(doc.Id).ConfigureAwait(false);
            var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (oldRoot is null) continue;
            var newRoot = oldRoot.ReplaceNode(mds.ParameterList, newParamList);
            accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));
        }

        // 2. Walk every caller and update its argument list. Capture the original text on first
        //    visit per document so the end-of-pass diff is against the untouched baseline.
        var callers = await SymbolFinder.FindCallersAsync(method, accumulator, ct).ConfigureAwait(false);
        foreach (var caller in callers)
        {
            foreach (var location in caller.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (!location.IsInSource) continue;
                var doc = accumulator.GetDocument(location.SourceTree);
                if (doc is null) continue;
                await CaptureOriginalAsync(doc.Id).ConfigureAwait(false);
                var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (oldRoot is null) continue;

                var node = oldRoot.FindNode(location.SourceSpan);
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is null) continue;

                var args = invocation.ArgumentList.Arguments;
                var isPositional = args.All(a => a.NameColon is null);
                var newArgs = updateCallsite(args, isPositional);
                if (newArgs.Equals(args)) continue;
                var newInvocation = invocation.WithArgumentList(invocation.ArgumentList.WithArguments(newArgs));
                var newRoot = oldRoot.ReplaceNode(invocation, newInvocation);
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));
            }
        }

        // 3. Emit ONE unified diff per touched file, original → final. DiffGenerator applies a
        //    16 KB cap with a truncation marker so the preview never blows past MCP payload budgets.
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

        return (accumulator, changes);
    }

    private RefactoringPreviewDto BuildPreviewDto(string workspaceId, Solution accumulator, List<FileChangeDto> changes, string description)
    {
        if (changes.Count == 0)
            throw new InvalidOperationException("change_signature_preview produced no changes — verify the symbol actually has callers.");
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }
}
