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
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var baseMembers = SymbolServiceHelpers.GetBaseMembers(symbol).Select(baseMember => SymbolMapper.ToDto(baseMember, solution)).ToList();
        var overrides = (await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false))
            .Select(overrideSymbol => SymbolMapper.ToDto(overrideSymbol, solution))
            .ToList();

        return new MemberHierarchyDto(SymbolMapper.ToDto(symbol, solution), baseMembers, overrides);
    }

    public async Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        symbol = await PromoteToDeclaringMemberIfRequestedAsync(solution, locator, symbol, preferDeclaringMember, ct).ConfigureAwait(false);

        var definitions = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(location => location.IsInSource))
        {
            var document = solution.GetDocument(location.SourceTree!);
            var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct).ConfigureAwait(false) : null;
            definitions.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        var referencesTask = _referenceService.FindReferencesAsync(workspaceId, locator, ct);
        var implementationsTask = _referenceService.FindImplementationsAsync(workspaceId, locator, ct);
        var baseMembersTask = _referenceService.FindBaseMembersAsync(workspaceId, locator, ct);
        var overridesTask = _referenceService.FindOverridesAsync(workspaceId, locator, ct);
        await Task.WhenAll(referencesTask, implementationsTask, baseMembersTask, overridesTask).ConfigureAwait(false);

        return new SymbolRelationshipsDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            Definitions: definitions,
            References: await referencesTask.ConfigureAwait(false) ?? [],
            Implementations: await implementationsTask.ConfigureAwait(false) ?? [],
            BaseMembers: await baseMembersTask.ConfigureAwait(false) ?? [],
            Overrides: await overridesTask.ConfigureAwait(false) ?? []);
    }

    public async Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
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
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
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

                callees.Add(SymbolMapper.ToLocationDto(invokedLoc, invokedSymbol));
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
