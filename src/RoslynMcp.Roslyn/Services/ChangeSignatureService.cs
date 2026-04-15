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
/// <c>reorder</c> is reserved for a follow-up PR — this release surfaces the surface but
/// errors with a clear message when invoked.
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
            "reorder" => throw new NotSupportedException(
                "change_signature_preview op='reorder' is not yet implemented. " +
                "Use op='add' / 'remove' / 'rename' or fall back to manual edits via preview_multi_file_edit."),
            _ => throw new ArgumentException(
                $"Unsupported op '{request.Op}'. Valid values: add, remove, rename, reorder.", nameof(request)),
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

        var paramText = string.IsNullOrWhiteSpace(request.DefaultValue)
            ? $"{request.ParameterType} {request.Name}"
            : $"{request.ParameterType} {request.Name} = {request.DefaultValue}";
        var argText = request.DefaultValue ?? "default";

        var (accumulator, changes) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: (existingParams) =>
            {
                var newParams = existingParams.Insert(insertPosition,
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(request.Name)).WithType(SyntaxFactory.ParseTypeName(request.ParameterType + " ")));
                return ParameterListFromText(newParams);
            },
            updateCallsite: (args, isPositional) =>
            {
                if (isPositional)
                {
                    var newArg = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(argText));
                    return args.Insert(insertPosition, newArg);
                }
                // Caller uses named args — splice by name so we don't break existing positions.
                var named = SyntaxFactory.Argument(
                    SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(request.Name!)),
                    refKindKeyword: default,
                    expression: SyntaxFactory.ParseExpression(argText));
                return args.Add(named);
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
            updateDeclaration: existing => ParameterListFromText(existing.RemoveAt(index)),
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
        Func<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax>, ParameterListSyntax> updateDeclaration,
        Func<Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax>, bool, Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        var accumulator = solution;
        var changes = new List<FileChangeDto>();

        // 1. Update the declaration(s). Most methods have one source declaration; partial methods
        //    can have two (definition + implementation) — both need updating.
        foreach (var declRef in method.DeclaringSyntaxReferences)
        {
            var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (node is not BaseMethodDeclarationSyntax mds) continue;
            var existing = System.Collections.Immutable.ImmutableArray.CreateRange(mds.ParameterList.Parameters);
            var newParamList = updateDeclaration(existing);
            var doc = accumulator.GetDocument(node.SyntaxTree);
            if (doc is null) continue;
            var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (oldRoot is null) continue;
            var newRoot = oldRoot.ReplaceNode(mds.ParameterList, newParamList);
            var newText = newRoot.ToFullString();
            var oldText = oldRoot.ToFullString();
            accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newText));
            changes.Add(new FileChangeDto(doc.FilePath ?? doc.Name, BuildMinimalDiff(doc.FilePath ?? doc.Name, oldText, newText)));
        }

        // 2. Walk every caller and update its argument list.
        var callers = await SymbolFinder.FindCallersAsync(method, accumulator, ct).ConfigureAwait(false);
        foreach (var caller in callers)
        {
            foreach (var location in caller.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (!location.IsInSource) continue;
                var doc = accumulator.GetDocument(location.SourceTree);
                if (doc is null) continue;
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
                var newText = newRoot.ToFullString();
                var oldText = oldRoot.ToFullString();
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newText));

                // Aggregate per-file (avoid duplicate diffs when multiple callsites in the same file).
                var existing = changes.FirstOrDefault(c => string.Equals(c.FilePath, doc.FilePath, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    changes.Add(new FileChangeDto(doc.FilePath ?? doc.Name, BuildMinimalDiff(doc.FilePath ?? doc.Name, oldText, newText)));
                }
                else
                {
                    var idx = changes.IndexOf(existing);
                    changes[idx] = existing with { UnifiedDiff = BuildMinimalDiff(doc.FilePath ?? doc.Name, oldText, newText) };
                }
            }
        }

        return (accumulator, changes);
    }

    private static ParameterListSyntax ParameterListFromText(System.Collections.Immutable.ImmutableArray<ParameterSyntax> parameters)
    {
        return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
    }

    private static string BuildMinimalDiff(string filePath, string before, string after)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("--- ").Append(filePath).Append('\n');
        sb.Append("+++ ").Append(filePath).Append('\n');
        sb.Append("@@ (change_signature) @@\n");
        foreach (var line in before.Split('\n')) sb.Append('-').Append(line).Append('\n');
        foreach (var line in after.Split('\n')) sb.Append('+').Append(line).Append('\n');
        return sb.ToString();
    }

    private RefactoringPreviewDto BuildPreviewDto(string workspaceId, Solution accumulator, List<FileChangeDto> changes, string description)
    {
        if (changes.Count == 0)
            throw new InvalidOperationException("change_signature_preview produced no changes — verify the symbol actually has callers.");
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }
}
