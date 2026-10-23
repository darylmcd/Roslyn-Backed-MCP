using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Supports <c>add</c>, <c>remove</c>, <c>rename</c>, and
/// <c>reorder</c> ops by rewriting both the method declaration and every callsite in one
/// atomic preview. Reorder accepts a <c>NewOrder</c> permutation of parameter names or
/// 0-based indices; mixed positional+named callsites raise an actionable error.
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
            throw new PublicArgumentException(
                "Parameter 'op' is required. Valid values: add, remove, rename, reorder.", "op");

        // Parenthesized display signatures are not metadata names; retain actionable guidance.
        if (locator.HasMetadataName && locator.MetadataName!.Contains('('))
        {
            throw new PublicArgumentException(
                "Parameter 'metadataName' must be a bare method name (e.g. 'Foo.Bar.Baz') with file/line/column for " +
                "disambiguation, OR a symbolHandle from symbol_search; a parenthesized signature is not accepted. " +
                "Drop the '(...)' or use symbolHandle.",
                "metadataName");
        }

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

        if (symbol is INamedTypeSymbol namedType)
            symbol = namedType.InstanceConstructors.SingleOrDefault(IsPrimaryConstructor) ?? symbol;
        if (symbol is IMethodSymbol primaryCtor && IsPrimaryConstructor(primaryCtor)
            && (primaryCtor.ContainingType.IsRecord || request.Op.ToLowerInvariant() is not ("add" or "remove")))
            throw PrimaryConstructorRefusal(primaryCtor.ContainingType);

        if (symbol is not IMethodSymbol method)
            throw new PublicInvalidOperationException(
                $"change_signature_preview requires a method, constructor, or primary-constructor type; the locator resolved " +
                $"a {symbol?.Kind.ToString() ?? "null"} symbol instead. Point filePath/line/column at the declaration or pass a method symbolHandle.");

        return request.Op.ToLowerInvariant() switch
        {
            "add" => await PreviewAddParameterAsync(workspaceId, solution, method, request, ct).ConfigureAwait(false),
            "remove" => await PreviewRemoveParameterAsync(workspaceId, solution, method, request, ct).ConfigureAwait(false),
            "rename" => await PreviewRenameParameterAsync(workspaceId, method, request, ct).ConfigureAwait(false),
            "reorder" => await PreviewReorderParametersAsync(workspaceId, solution, method, request, ct).ConfigureAwait(false),
            _ => throw new PublicArgumentException(
                "Parameter 'op' is not supported. Valid values: add, remove, rename, reorder.", "op"),
        };
    }

    /// <summary>
    /// True when <paramref name="method"/> is the constructor declared by a type's parameter list
    /// (<c>record R(int X)</c>, <c>class C(int x)</c>, <c>struct S(int x)</c>). The compiler-synthesized
    /// record copy constructor also lists the record in its syntax references, so it is excluded by
    /// the parameter-list match (it takes the record itself, never the declared parameters).
    /// </summary>
    private static bool IsPrimaryConstructor(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Constructor || method.IsStatic)
            return false;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax { ParameterList: { } list }
                && !method.IsImplicitlyDeclared
                && list.Parameters.Count == method.Parameters.Length
                && method.Parameters.All(parameter => parameter.DeclaringSyntaxReferences.Any(parameterReference =>
                    parameterReference.SyntaxTree == list.SyntaxTree
                    && list.Parameters[parameter.Ordinal].Span.Contains(parameterReference.Span))))
            {
                return true;
            }
        }

        return false;
    }

    private static PublicInvalidOperationException PrimaryConstructorRefusal(INamedTypeSymbol type)
    {
        var recordNote = type.IsRecord
            ? " For a record these parameters are also synthesized public properties, so a signature edit would change its public shape."
            : string.Empty;
        return new PublicInvalidOperationException(
            $"change_signature_preview does not support primary-constructor parameters ('{type.Name}'); " +
            $"edit the parameter list on the type declaration directly.{recordNote}");
    }

    private async Task<RefactoringPreviewDto> PreviewAddParameterAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new PublicArgumentException("op='add' requires parameter 'name' (the new parameter's name).", "name");
        if (string.IsNullOrWhiteSpace(request.ParameterType))
            throw new PublicArgumentException("op='add' requires parameter 'parameterType'.", "parameterType");

        // change-signature-preview-add-unhelpful-error:
        // When the caller does not supply Position, default to end-append (the intent is
        // unambiguous — "add a new parameter named X of type Y" means "append it"). When
        // the caller supplies a Position that exceeds Parameters.Length, raise an
        // actionable error citing the user-facing fields (Name + ParameterType + the
        // valid Position range) instead of bubbling an internal ArgumentOutOfRangeException
        // with paramName='index'. Pre-fix this surfaced as
        // "Parameter 'index' has an out-of-range value" and forced agents to guess.
        var insertPosition = request.Position ?? method.Parameters.Length;
        if (insertPosition < 0 || insertPosition > method.Parameters.Length)
            throw new PublicArgumentException(
                $"op='add' parameter 'position' ({insertPosition}) is out of range for {method.ToDisplayString()} " +
                $"(method has {method.Parameters.Length} existing parameter(s); " +
                $"valid position is 0..{method.Parameters.Length}, or omit position to append at end).",
                "position");

        // change-signature-preview-brace-concat-on-add-op:
        // Parse only the new parameter with SyntaxFactory.ParseParameterList — the parser produces a
        // properly-tokenized ParameterSyntax (type + space + identifier + optional `= value`).
        // The previous implementation used SyntaxFactory.Parameter + ParseTypeName(type + " ")
        // which left the parameter without inter-token trivia, so the rendered preview ran
        // tokens together (e.g. `intx` instead of `int x`) and dropped any default value.
        var paramText = string.IsNullOrWhiteSpace(request.DefaultValue)
            ? $"{request.ParameterType} {request.Name}"
            : $"{request.ParameterType} {request.Name} = {request.DefaultValue}";
        var argText = request.DefaultValue ?? "default";

        var (accumulator, changes, callsiteUpdates, warnings) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: (existingList) =>
            {
                // Edit syntax nodes so retained parameter and separator trivia survives.
                return EditParameterList(existingList, addParam: paramText, addAt: insertPosition, removeAt: null);
            },
            updateCallsite: (context, args) => context.Add(args, insertPosition, request.Name!, argText),
            ct).ConfigureAwait(false);

        return BuildPreviewDto(workspaceId, solution, accumulator, changes, callsiteUpdates, warnings,
            $"Add parameter '{request.Name}: {request.ParameterType}' at position {insertPosition} of {method.ToDisplayString()}");
    }

    private async Task<RefactoringPreviewDto> PreviewRemoveParameterAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        var index = ResolveParameterIndex(method, request);
        if (index < 0) throw new PublicArgumentException(
            $"op='remove' requires 'position' (0..{method.Parameters.Length - 1}) or 'name' matching a parameter of " +
            $"{method.ToDisplayString()} (parameters: {string.Join(", ", method.Parameters.Select(p => p.Name))}).",
            "position");

        var (accumulator, changes, callsiteUpdates, warnings) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: existingList => EditParameterList(existingList, addParam: null, addAt: null, removeAt: index),
            updateCallsite: (context, args) => context.Remove(args, index),
            ct).ConfigureAwait(false);

        return BuildPreviewDto(workspaceId, solution, accumulator, changes, callsiteUpdates, warnings,
            $"Remove parameter '{method.Parameters[index].Name}' (position {index}) from {method.ToDisplayString()}");
    }

    private static ParameterListSyntax EditParameterList(
        ParameterListSyntax existingList, string? addParam, int? addAt, int? removeAt)
    {
        var parameters = existingList.Parameters;
        if (removeAt is int remove) parameters = parameters.RemoveAt(remove);
        if (addAt is int insert && addParam is not null)
        {
            var parsed = SyntaxFactory.ParseParameterList("(" + addParam + ")");
            if (parsed.ContainsDiagnostics || parsed.Parameters.Count != 1)
                throw new PublicInvalidOperationException("The new parameter is not valid C# parameter syntax.");
            parameters = parameters.Insert(insert, parsed.Parameters[0].WithLeadingTrivia(insert == 0 ? default : SyntaxFactory.Space));
        }
        return existingList.WithParameters(NormalizeParameterWhitespace(parameters));
    }

    /// <summary>
    /// Rename of a method parameter delegates to the existing rename engine on the parameter
    /// symbol — that engine already handles named-argument call sites correctly.
    /// </summary>
    private async Task<RefactoringPreviewDto> PreviewRenameParameterAsync(
        string workspaceId, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new PublicArgumentException("op='rename' requires parameter 'name' (the current parameter name).", "name");
        if (string.IsNullOrWhiteSpace(request.NewName))
            throw new PublicArgumentException("op='rename' requires parameter 'newName'.", "newName");

        var paramSymbol = method.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, request.Name, StringComparison.Ordinal));
        if (paramSymbol is null)
            throw new PublicArgumentException(
                $"Parameter 'name' does not match a parameter of {method.ToDisplayString()} " +
                $"(parameters: {string.Join(", ", method.Parameters.Select(p => p.Name))}).", "name");

        var loc = paramSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null)
            throw new PublicInvalidOperationException(
                "op='rename' requires a method declared in source; the resolved method comes from a referenced assembly. " +
                "Point filePath/line/column at a source-declared method.");
        var lineSpan = loc.GetLineSpan();
        var renameLocator = SymbolLocator.BySource(loc.SourceTree!.FilePath, lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Character + 1);
        return await _refactoringService.PreviewRenameAsync(workspaceId, renameLocator, request.NewName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reorder method parameters according to <see cref="ChangeSignatureRequest.NewOrder"/>.
    /// <para>
    /// <c>NewOrder</c> is a comma-separated list that must be a permutation of the existing
    /// parameters: each token is either a parameter name or a 0-based index. The declaration
    /// is rewritten to match. Per-callsite handling:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>All-positional callsite — arguments are named without changing expression evaluation order.</description></item>
    ///   <item><description>All-named callsite — retains argument order and bindings; omitted compiler-supplied values are materialized.</description></item>
    ///   <item><description>Mixed positional + named callsite — refused with actionable guidance to normalize argument naming before retrying.</description></item>
    /// </list>
    /// <para>
    /// <c>params</c> arrays MUST remain in the trailing position (otherwise the C# parser
    /// rejects them); we refuse if the new order moves a <c>params</c> parameter off the
    /// trailing slot.
    /// </para>
    /// </summary>
    private async Task<RefactoringPreviewDto> PreviewReorderParametersAsync(
        string workspaceId, Solution solution, IMethodSymbol method, ChangeSignatureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewOrder))
            throw new PublicArgumentException(
                "op='reorder' requires parameter 'newOrder' — a comma-separated permutation of the existing parameters " +
                "(by name or 0-based index, e.g. 'b,a,c' or '1,0,2').",
                "newOrder");

        var permutation = ParseAndValidatePermutation(method, request.NewOrder!);

        // Check params trailing-slot invariant. params must stay last.
        for (var newIdx = 0; newIdx < permutation.Length; newIdx++)
        {
            var origIdx = permutation[newIdx];
            if (method.Parameters[origIdx].IsParams && newIdx != permutation.Length - 1)
            {
                throw new PublicArgumentException(
                    $"op='reorder' cannot move the params parameter '{method.Parameters[origIdx].Name}' off the " +
                    $"trailing slot — C# requires params to be the last parameter. Keep '{method.Parameters[origIdx].Name}' " +
                    $"at the end of newOrder.",
                    "newOrder");
            }
        }

        // Identity permutation = no-op; refuse so we don't produce an empty preview.
        var isIdentity = true;
        for (var i = 0; i < permutation.Length; i++)
        {
            if (permutation[i] != i) { isIdentity = false; break; }
        }
        if (isIdentity)
            throw new PublicArgumentException(
                "op='reorder' newOrder is the identity permutation — no change to apply.",
                "newOrder");

        var (accumulator, changes, callsiteUpdates, warnings) = await ApplyAddRemoveAsync(
            solution, method,
            updateDeclaration: existingList => BuildReorderedParameterList(existingList, permutation),
            updateCallsite: (context, args) => context.Reorder(args),
            ct).ConfigureAwait(false);

        var orderDescription = string.Join(", ",
            permutation.Select(i => method.Parameters[i].Name));
        return BuildPreviewDto(workspaceId, solution, accumulator, changes, callsiteUpdates, warnings,
            $"Reorder parameters of {method.ToDisplayString()} to ({orderDescription})");
    }

    /// <summary>
    /// Parses <paramref name="newOrder"/> into a permutation array. Each entry maps a new
    /// position to the original parameter index. Tokens may be parameter names or 0-based
    /// indices; the result must be a strict permutation of <c>[0..method.Parameters.Length)</c>.
    /// </summary>
    private static int[] ParseAndValidatePermutation(IMethodSymbol method, string newOrder)
    {
        var tokens = newOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != method.Parameters.Length)
            throw new PublicArgumentException(
                $"op='reorder' newOrder must list every parameter exactly once. " +
                $"Method {method.ToDisplayString()} has {method.Parameters.Length} parameter(s) " +
                $"({string.Join(", ", method.Parameters.Select(p => p.Name))}); newOrder supplied " +
                $"{tokens.Length} token(s).",
                "newOrder");

        var permutation = new int[tokens.Length];
        var seen = new HashSet<int>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            int origIdx;
            if (int.TryParse(token, out var asIndex))
            {
                if (asIndex < 0 || asIndex >= method.Parameters.Length)
                    throw new PublicArgumentException(
                        $"op='reorder' newOrder index {asIndex} is out of range (valid 0..{method.Parameters.Length - 1}).",
                        "newOrder");
                origIdx = asIndex;
            }
            else
            {
                origIdx = -1;
                for (var p = 0; p < method.Parameters.Length; p++)
                {
                    if (string.Equals(method.Parameters[p].Name, token, StringComparison.Ordinal))
                    {
                        origIdx = p;
                        break;
                    }
                }
                if (origIdx < 0)
                    throw new PublicArgumentException(
                        $"op='reorder' newOrder entry {i + 1} does not match any parameter on " +
                        $"{method.ToDisplayString()} (parameters: {string.Join(", ", method.Parameters.Select(p => p.Name))}).",
                        "newOrder");
            }
            if (!seen.Add(origIdx))
                throw new PublicArgumentException(
                    $"op='reorder' newOrder lists parameter '{method.Parameters[origIdx].Name}' more than once.",
                    "newOrder");
            permutation[i] = origIdx;
        }
        return permutation;
    }

    /// <summary>
    /// Build a new <see cref="ParameterListSyntax"/> by reordering the existing parameters
    /// according to <paramref name="permutation"/>. Retains original syntax nodes,
    /// separator trivia, and enclosing parentheses.
    /// </summary>
    private static ParameterListSyntax BuildReorderedParameterList(
        ParameterListSyntax existingList, int[] permutation)
    {
        var parameters = SyntaxFactory.SeparatedList(permutation.Select(i => existingList.Parameters[i]), existingList.Parameters.GetSeparators());
        return existingList.WithParameters(NormalizeParameterWhitespace(parameters));
    }

    private static SeparatedSyntaxList<ParameterSyntax> NormalizeParameterWhitespace(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        return SyntaxFactory.SeparatedList(parameters.Select((parameter, index) =>
        {
            var trivia = parameter.GetLeadingTrivia();
            if (trivia.All(t => t.IsKind(SyntaxKind.WhitespaceTrivia)))
                return parameter.WithLeadingTrivia(index == 0 || parameters.GetSeparator(index - 1).TrailingTrivia.ToFullString().Any(char.IsWhiteSpace) ? default : SyntaxFactory.Space);
            return parameter;
        }), parameters.GetSeparators());
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

    private async Task<(Solution Accumulator, List<FileChangeDto> Changes, List<CallsiteUpdateDto> CallsiteUpdates, List<string> Warnings)> ApplyAddRemoveAsync(
        Solution solution,
        IMethodSymbol method,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<ChangeSignatureAddRemovePreviewBuilder.BoundCallContext, SeparatedSyntaxList<ArgumentSyntax>, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        return await ChangeSignatureAddRemovePreviewBuilder.BuildAsync(
            solution,
            method,
            updateDeclaration,
            updateCallsite,
            ct).ConfigureAwait(false);
    }

    private RefactoringPreviewDto BuildPreviewDto(
        string workspaceId,
        Solution originalSolution,
        Solution accumulator,
        List<FileChangeDto> changes,
        List<CallsiteUpdateDto> callsiteUpdates,
        List<string> warnings,
        string description)
    {
        if (changes.Count == 0)
            throw new PublicInvalidOperationException("The signature change produced no editable declaration changes. Choose a source declaration and a non-identity operation.");
        var token = _previewStore.Store(workspaceId, originalSolution, accumulator, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.ChangeSignature);
        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            Warnings: warnings.Count == 0 ? null : warnings,
            CallsiteUpdates: callsiteUpdates.Count == 0 ? null : callsiteUpdates);
    }
}
