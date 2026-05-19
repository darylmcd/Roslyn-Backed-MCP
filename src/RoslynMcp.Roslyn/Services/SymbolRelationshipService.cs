using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class SymbolRelationshipService : ISymbolRelationshipService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IReferenceService _referenceService;
    private readonly ILogger<SymbolRelationshipService> _logger;

    public SymbolRelationshipService(IWorkspaceManager workspace, IReferenceService referenceService, ILogger<SymbolRelationshipService> logger)
    {
        _workspace = workspace;
        _referenceService = referenceService;
        _logger = logger;
    }

    public async Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("SymbolRelationshipService.GetTypeHierarchyAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol namedType) return null;

        // BUG-006: For interface symbols, FindDerivedClassesAsync returns nothing (it only walks
        // class inheritance) and namedType.BaseType is also null, so the legacy implementation
        // produced an empty hierarchy for every interface. Use FindImplementationsAsync to find
        // implementing types and FindDerivedInterfacesAsync to find sub-interfaces, and treat
        // namedType.Interfaces (the interfaces this interface extends) as base types.
        var isInterface = namedType.TypeKind == TypeKind.Interface;

        var baseTypes = new List<TypeHierarchyDto>();
        if (isInterface)
        {
            // An interface's "base types" are the interfaces it directly extends.
            foreach (var baseInterface in namedType.Interfaces)
            {
                baseTypes.Add(BuildHierarchyEntry(baseInterface));
            }
        }
        else
        {
            var current = namedType.BaseType;
            while (current is not null && current.SpecialType != SpecialType.System_Object)
            {
                baseTypes.Add(BuildHierarchyEntry(current));
                current = current.BaseType;
            }
        }

        var derivedTypes = new List<TypeHierarchyDto>();
        if (isInterface)
        {
            // Implementing types: classes/structs that say `: IFoo` (or inherit a class that does).
            var implementations = await SymbolFinder.FindImplementationsAsync(
                namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var impl in implementations.OfType<INamedTypeSymbol>())
            {
                derivedTypes.Add(BuildHierarchyEntry(impl));
            }

            // Sub-interfaces: interfaces that extend this one.
            var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(
                namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var derivedInterface in derivedInterfaces)
            {
                derivedTypes.Add(BuildHierarchyEntry(derivedInterface));
            }
        }
        else
        {
            var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(
                namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var derivedClass in derivedClasses)
            {
                derivedTypes.Add(BuildHierarchyEntry(derivedClass));
            }
        }

        // For non-interface types this is the implemented interfaces. For interfaces themselves
        // we already promoted the directly-extended interfaces to BaseTypes above, so leave the
        // Interfaces bucket empty to avoid duplication.
        var interfacesList = isInterface
            ? new List<TypeHierarchyDto>()
            : namedType.Interfaces.Select(BuildHierarchyEntry).ToList();

        var selfLoc = namedType.Locations.FirstOrDefault(l => l.IsInSource);
        return new TypeHierarchyDto(
            namedType.Name, namedType.ToDisplayString(),
            selfLoc?.GetLineSpan().Path, selfLoc?.GetLineSpan().StartLinePosition.Line + 1,
            baseTypes,
            derivedTypes,
            interfacesList);
    }

    private static TypeHierarchyDto BuildHierarchyEntry(INamedTypeSymbol type)
    {
        var loc = type.Locations.FirstOrDefault(l => l.IsInSource);
        return new TypeHierarchyDto(
            type.Name,
            type.ToDisplayString(),
            loc?.GetLineSpan().Path,
            loc?.GetLineSpan().StartLinePosition.Line + 1,
            [],
            [],
            []);
    }

    public async Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("SymbolRelationshipService.GetMemberHierarchyAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        // member-hierarchy-overrides-mislabels-sibling-interface-impls (gh #736, gh #737):
        // member_hierarchy now exposes true overrides and sibling interface implementations
        // as two distinct buckets so callers can tell the difference between "this is the
        // override chain" and "these are the unrelated types that happen to satisfy the
        // same interface contract". Run both lookups in parallel — they hit disjoint
        // Roslyn APIs.
        var baseMembers = SymbolServiceHelpers.GetBaseMembers(symbol).Select(baseMember => SymbolMapper.ToDto(baseMember, solution)).ToList();
        var overridesTask = _referenceService.FindOverridesAsync(workspaceId, locator, ct);
        var siblingImplsTask = _referenceService.FindSiblingInterfaceImplementationsAsync(workspaceId, locator, ct);
        await Task.WhenAll(overridesTask, siblingImplsTask).ConfigureAwait(false);

        return new MemberHierarchyDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            BaseMembers: baseMembers,
            Overrides: await overridesTask.ConfigureAwait(false),
            SiblingInterfaceImplementations: await siblingImplsTask.ConfigureAwait(false));
    }

    public async Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct)
    {
        _logger.LogDebug("SymbolRelationshipService.GetSymbolRelationshipsAsync: workspaceId={WorkspaceId} locator={Locator} preferDeclaringMember={PreferDeclaringMember}", workspaceId, locator, preferDeclaringMember);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var originalSymbol = symbol;
        symbol = await PromoteToDeclaringMemberIfRequestedAsync(solution, locator, symbol, preferDeclaringMember, ct).ConfigureAwait(false);

        // `symbol-relationships-builtin-type-unbounded-enumeration` (gh #757): when the caret lands
        // on a builtin-type token (e.g. `void`, `int`) and `preferDeclaringMember=false`, the
        // promotion above intentionally short-circuits and we fall through to FindReferencesAsync
        // on `System.Void`/`System.Int32`, which enumerates every reference to the special type
        // solution-wide (measured 57+ KB on an 11-project / 759-document solution). Detect the
        // post-promotion builtin and return an empty envelope with a Hint explaining the
        // suppression. The `preferDeclaringMember=true` auto-promotion path is unaffected because
        // by then the symbol has been swapped to the enclosing member.
        if (!preferDeclaringMember && symbol is INamedTypeSymbol namedSym && namedSym.SpecialType != SpecialType.None)
        {
            return new SymbolRelationshipsDto(
                Symbol: SymbolMapper.ToDto(symbol, solution),
                Definitions: [],
                References: [],
                Implementations: [],
                BaseMembers: [],
                Overrides: [],
                Hint: "Resolved to builtin type — references list suppressed. Set preferDeclaringMember=true or relocate cursor to a non-builtin token.");
        }

        var relationshipLocator = SymbolEqualityComparer.Default.Equals(originalSymbol, symbol)
            ? locator
            : CreateLocatorForPromotedSymbol(symbol, locator);

        var definitions = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(location => location.IsInSource))
        {
            var document = solution.GetDocument(location.SourceTree!);
            var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct).ConfigureAwait(false) : null;
            definitions.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        var referencesTask = _referenceService.FindReferencesAsync(workspaceId, relationshipLocator, ct);
        var implementationsTask = _referenceService.FindImplementationsAsync(workspaceId, relationshipLocator, ct);
        var baseMembersTask = _referenceService.FindBaseMembersAsync(workspaceId, relationshipLocator, ct);
        var overridesTask = _referenceService.FindOverridesAsync(workspaceId, relationshipLocator, ct);
        await Task.WhenAll(referencesTask, implementationsTask, baseMembersTask, overridesTask).ConfigureAwait(false);

        return new SymbolRelationshipsDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            Definitions: definitions,
            References: await referencesTask.ConfigureAwait(false) ?? [],
            Implementations: await implementationsTask.ConfigureAwait(false) ?? [],
            BaseMembers: await baseMembersTask.ConfigureAwait(false) ?? [],
            Overrides: await overridesTask.ConfigureAwait(false) ?? []);
    }

    private static SymbolLocator CreateLocatorForPromotedSymbol(ISymbol symbol, SymbolLocator fallback)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null)
        {
            return fallback;
        }

        var lineSpan = location.GetLineSpan();
        if (string.IsNullOrWhiteSpace(lineSpan.Path))
        {
            return fallback;
        }

        return SymbolLocator.BySource(
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    public async Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct)
    {
        _logger.LogDebug("SymbolRelationshipService.GetSignatureHelpAsync: workspaceId={WorkspaceId} locator={Locator} preferDeclaringMember={PreferDeclaringMember}", workspaceId, locator, preferDeclaringMember);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);

        // `symbol-signature-help-returns-bare-null-for-resolvable-method-metadata` (gh #747):
        // mirrors the gh #616 fallback for `callers_callees`. When the caller supplies a fully
        // qualified method signature like `Ns.Type.Method(Ns.ParamType, System.Threading.CancellationToken)`,
        // `SymbolResolver.ResolveByMetadataNameAsync` returns null because it splits on the LAST dot —
        // which lands inside the parenthesized parameter list (`System.Threading.CancellationToken`),
        // producing a bogus containing-type name. The qualified-signature fallback strips the parameter
        // list, retries the resolve, and picks the matching overload.
        if (symbol is null && locator.HasMetadataName)
        {
            symbol = await TryResolveByQualifiedSignatureAsync(solution, locator.MetadataName!, ct).ConfigureAwait(false);
        }

        if (symbol is null)
        {
            return null;
        }

        symbol = await PromoteToDeclaringMemberIfRequestedAsync(solution, locator, symbol, preferDeclaringMember, ct).ConfigureAwait(false);

        var dto = SymbolMapper.ToDto(symbol, solution);
        var parameters = symbol is IMethodSymbol method
            ? method.Parameters
                .Select(parameter => $"{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}")
                .ToList()
            : dto.Parameters ?? [];

        return new SignatureHelpDto(
            DisplaySignature: symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ReturnType: dto.ReturnType,
            Parameters: parameters,
            Documentation: dto.Documentation);
    }

    public async Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("SymbolRelationshipService.GetCallersCalleesAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);

        // `callers-callees-rejects-fully-qualified-names` (gh #616): if the caller supplied a fully
        // qualified method signature like `Ns.Type.Method(Ns.ParamType, System.Threading.CancellationToken)`,
        // `SymbolResolver.ResolveByMetadataNameAsync` returns null because it splits on the LAST dot —
        // which lands inside the parenthesized parameter list (`System.Threading.CancellationToken`),
        // producing a bogus containing-type name. Strip the parameter list, retry the resolve, and if
        // multiple overloads exist, prefer the one whose parameter types match the supplied signature.
        // Sibling tools (`find_references`, `find_type_mutations`) reach the disambiguation path on the
        // last-dot-split path for typical inputs; this branch only fires when the supplied input has
        // unbalanced/inside-parens dots that defeat that path entirely.
        if (symbol is null && locator.HasMetadataName)
        {
            symbol = await TryResolveByQualifiedSignatureAsync(solution, locator.MetadataName!, ct).ConfigureAwait(false);
        }

        if (symbol is null) return null;

        // If the resolved symbol is a type (e.g. Task<T> from an async method's return type),
        // or a constructor invoked at the caret (e.g. new InvalidOperationException(...)),
        // resolve the enclosing method for callers/callees analysis.
        if (locator.HasSourceLocation &&
            (symbol is INamedTypeSymbol ||
             (symbol is IMethodSymbol methodAtCaret && methodAtCaret.MethodKind == MethodKind.Constructor)))
        {
            var enclosingMethod = await TryResolveEnclosingMethodAsync(solution, locator, ct).ConfigureAwait(false);
            if (enclosingMethod is not null)
                symbol = enclosingMethod;
        }

        var callers = await CollectCallersAsync(symbol, solution, ct).ConfigureAwait(false);
        var callees = symbol is IMethodSymbol methodSymbol
            ? await CollectCalleesAsync(methodSymbol, solution, ct).ConfigureAwait(false)
            : [];

        return new CallerCalleeDto(
            SymbolMapper.ToDto(symbol, solution),
            callers,
            callees);
    }

    private static async Task<List<LocationDto>> CollectCallersAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var callers = new List<LocationDto>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(
                    refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                if (containingSymbol is not null && !SymbolEqualityComparer.Default.Equals(containingSymbol, symbol))
                {
                    var preview = await SymbolResolver.GetPreviewTextAsync(
                        refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                    callers.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));
                }
            }
        }
        return callers;
    }

    private static async Task<List<LocationDto>> CollectCalleesAsync(IMethodSymbol methodSymbol, Solution solution, CancellationToken ct)
    {
        var callees = new List<LocationDto>();
        var seenCalleeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in methodSymbol.Locations.Where(l => l.IsInSource))
        {
            var tree = location.SourceTree;
            if (tree is null) continue;

            var doc = solution.GetDocument(tree);
            if (doc is null) continue;

            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel is null) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            var methodNode = root.FindNode(location.SourceSpan);

            foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var invokedSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol;
                if (invokedSymbol is null) continue;

                var invokedLoc = invokedSymbol.Locations.FirstOrDefault(l => l.IsInSource) ?? invocation.GetLocation();
                var lineSpan = invokedLoc.GetLineSpan();
                var dedupeKey =
                    $"{invokedSymbol.ToDisplayString()}|{lineSpan.Path}|{lineSpan.StartLinePosition.Line}|{lineSpan.StartLinePosition.Character}";
                if (!seenCalleeKeys.Add(dedupeKey)) continue;

                var calleeDoc = invokedLoc.SourceTree is { } calleeTree ? solution.GetDocument(calleeTree) : doc;
                var previewText = calleeDoc is not null
                    ? await SymbolResolver.GetPreviewTextAsync(calleeDoc, invokedLoc, ct).ConfigureAwait(false)
                    : null;

                callees.Add(SymbolMapper.ToLocationDto(invokedLoc, invokedSymbol, previewText));
            }
        }
        return callees;
    }

    /// <summary>
    /// FLAG-006: shared helper that promotes a type-token resolution to the enclosing member when
    /// the caller requested <c>preferDeclaringMember</c>. Used by both <see cref="GetSignatureHelpAsync"/>
    /// and <see cref="GetSymbolRelationshipsAsync"/>.
    /// </summary>
    private static async Task<ISymbol> PromoteToDeclaringMemberIfRequestedAsync(
        Solution solution, SymbolLocator locator, ISymbol resolved, bool preferDeclaringMember, CancellationToken ct)
    {
        if (!preferDeclaringMember || resolved is not INamedTypeSymbol || !locator.HasSourceLocation)
            return resolved;

        var enclosing = await SymbolResolver.TryResolveEnclosingMemberAsync(
            solution, locator.FilePath!, locator.Line!.Value, locator.Column!.Value, ct).ConfigureAwait(false);
        return enclosing ?? resolved;
    }

    /// <summary>
    /// gh #616 / `callers-callees-rejects-fully-qualified-names`: resolves a fully qualified method
    /// signature of the shape <c>Namespace.Type.Method(Param1Type, Param2Type, ...)</c>. Strips the
    /// parameter-list suffix, resolves the containing type and member-name overload set via the
    /// standard metadata-name path, then narrows to the overload whose parameter types match the
    /// supplied signature (by display string, ignoring whitespace and an optional containing-namespace
    /// prefix on each parameter). Returns null if the input does not contain a parenthesized parameter
    /// list, the containing type does not exist, or no overload matches.
    /// </summary>
    /// <remarks>
    /// The matcher is best-effort: it tolerates the user pasting `System.Threading.CancellationToken`
    /// when the parameter is declared as `CancellationToken` (and vice versa) by comparing both the
    /// fully-qualified and short-name forms of each parameter's declared type. When the supplied
    /// signature is ambiguous (e.g. only a partial parameter list) and multiple overloads match, the
    /// first match in declaration order is returned — the caller is expected to use a more specific
    /// locator (symbolHandle or source position) if that picks the wrong overload.
    /// </remarks>
    internal static async Task<ISymbol?> TryResolveByQualifiedSignatureAsync(
        Solution solution, string metadataName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(metadataName)) return null;

        var openParen = metadataName.IndexOf('(');
        if (openParen <= 0) return null;
        var closeParen = metadataName.LastIndexOf(')');
        if (closeParen <= openParen) return null;

        var qualifiedMember = metadataName[..openParen].Trim();
        var paramList = metadataName.Substring(openParen + 1, closeParen - openParen - 1);

        var lastDot = qualifiedMember.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == qualifiedMember.Length - 1) return null;

        var containingTypeName = qualifiedMember[..lastDot];
        var memberName = qualifiedMember[(lastDot + 1)..];

        var expectedParams = SplitTopLevel(paramList);

        IMethodSymbol? firstOverload = null;
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var containingType = compilation.GetTypeByMetadataName(containingTypeName);
            if (containingType is null) continue;

            foreach (var member in containingType.GetMembers(memberName).OfType<IMethodSymbol>())
            {
                firstOverload ??= member;
                if (MatchesParameterSignature(member, expectedParams))
                {
                    return member;
                }
            }
        }

        // No signature match found. If the caller provided an empty parameter list `()`, fall through
        // to the first zero-arity overload; otherwise return null so the tool surfaces a NotFound rather
        // than silently returning the wrong overload.
        if (expectedParams.Count == 0 && firstOverload is not null && firstOverload.Parameters.Length == 0)
        {
            return firstOverload;
        }

        return null;
    }

    /// <summary>
    /// Splits a top-level comma-separated parameter list while respecting nested angle/parens —
    /// e.g. `List&lt;int, string&gt;, Func&lt;T, U&gt;` returns two entries, not five. Whitespace-trimmed
    /// entries; empty input returns an empty list.
    /// </summary>
    private static List<string> SplitTopLevel(string paramList)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(paramList)) return result;

        var depth = 0;
        var start = 0;
        for (var i = 0; i < paramList.Length; i++)
        {
            var ch = paramList[i];
            if (ch == '<' || ch == '(' || ch == '[') depth++;
            else if (ch == '>' || ch == ')' || ch == ']') depth--;
            else if (ch == ',' && depth == 0)
            {
                result.Add(paramList.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        var last = paramList[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }

    /// <summary>
    /// Returns true when <paramref name="method"/>'s parameter type display strings match
    /// <paramref name="expected"/> entry-for-entry. Each expected entry is compared against both the
    /// fully-qualified and the minimally-qualified form of the actual parameter type, so callers can
    /// paste either `System.Threading.CancellationToken` or `CancellationToken`.
    /// </summary>
    private static bool MatchesParameterSignature(IMethodSymbol method, List<string> expected)
    {
        if (method.Parameters.Length != expected.Count) return false;

        for (var i = 0; i < expected.Count; i++)
        {
            var paramType = method.Parameters[i].Type;
            // FullyQualifiedFormat emits `global::` qualifiers on every named type (including inside
            // generic argument lists). Pasted user signatures never include `global::`, so strip every
            // occurrence — not just a leading one — before comparing.
            var fullyQualified = paramType
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            var minimallyQualified = paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var shortName = paramType.Name;

            // Normalize whitespace inside generic argument lists — the user may paste
            // `List<int,string>` or `List< int, string >`; the compiler form has no spaces.
            var expectedEntry = NormalizeTypeForCompare(expected[i]);
            if (string.Equals(expectedEntry, NormalizeTypeForCompare(fullyQualified), StringComparison.Ordinal)) continue;
            if (string.Equals(expectedEntry, NormalizeTypeForCompare(minimallyQualified), StringComparison.Ordinal)) continue;
            if (string.Equals(expectedEntry, NormalizeTypeForCompare(shortName), StringComparison.Ordinal)) continue;

            return false;
        }

        return true;
    }

    private static string NormalizeTypeForCompare(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static async Task<IMethodSymbol?> TryResolveEnclosingMethodAsync(
        Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        if (locator.FilePath is null || locator.Line is null || locator.Column is null) return null;

        var enclosing = await SymbolResolver.TryResolveEnclosingMemberAsync(
            solution, locator.FilePath, locator.Line.Value, locator.Column.Value, ct).ConfigureAwait(false);

        if (enclosing is IMethodSymbol method)
            return method;

        return null;
    }
}
