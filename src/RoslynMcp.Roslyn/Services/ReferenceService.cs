using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class ReferenceService : IReferenceService, IPreResolvedReferenceService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<ReferenceService> _logger;

    public ReferenceService(IWorkspaceManager workspace, ILogger<ReferenceService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool summary = false, IReadOnlyCollection<string>? projectFilter = null)
    {
        _logger.LogDebug("ReferenceService.FindReferencesAsync: workspaceId={WorkspaceId} locator={Locator} summary={Summary} projectFilterCount={ProjectFilterCount}", workspaceId, locator, summary, projectFilter?.Count ?? 0);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Throw on unresolved symbol so callers get a structured NotFound envelope
        // instead of an empty list they cannot distinguish from "valid symbol, zero references".
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        // find-references-project-filter: when projectFilter is supplied, drop reference locations
        // whose document does not belong to a named project before materializing (saves the per-ref
        // syntax-root + preview-text fetches for filtered-out hits). Case-sensitive Project.Name
        // match — matches semantic_grep's existing projectFilter semantics. Null/empty filter is a
        // no-op so unfiltered behavior remains byte-identical.
        if (projectFilter is { Count: > 0 })
        {
            var filterSet = new HashSet<string>(projectFilter, StringComparer.Ordinal);
            refLocations = refLocations
                .Where(r => r.Document?.Project.Name is { } name && filterSet.Contains(name))
                .ToList();
        }
        var dtos = await ReferenceLocationMaterializer.MaterializeDtosAsync(refLocations, ct, summary).ConfigureAwait(false);
        // FLAG-8b-A: stable sort by (FilePath, StartLine, StartColumn) so parallel callers get
        // identical ordering. Roslyn's symbol-enumeration walker may otherwise produce different
        // orderings across concurrent invocations of the same query.
        return dtos
            .OrderBy(dto => dto.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(dto => dto.StartLine)
            .ThenBy(dto => dto.StartColumn)
            .ToList();
    }

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(
        string workspaceId,
        SymbolLocator locator,
        CancellationToken ct,
        bool includeGeneratedPartials = false)
    {
        _logger.LogDebug("ReferenceService.FindImplementationsAsync: workspaceId={WorkspaceId} locator={Locator} includeGeneratedPartials={IncludeGeneratedPartials}", workspaceId, locator, includeGeneratedPartials);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        return await FindImplementationsAsync(workspaceId, symbol, ct, includeGeneratedPartials).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(
        string workspaceId,
        ISymbol symbol,
        CancellationToken ct,
        bool includeGeneratedPartials = false)
    {
        _logger.LogDebug("ReferenceService.FindImplementationsAsync: workspaceId={WorkspaceId} symbol={Symbol} includeGeneratedPartials={IncludeGeneratedPartials}", workspaceId, symbol.ToDisplayString(), includeGeneratedPartials);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
        var results = new List<LocationDto>();

        // find-implementations-source-gen-partial-dedup: Roslyn merges partial-class
        // declarations into a single ISymbol whose Locations property exposes every
        // declaration site — including ones emitted by source generators like
        // [LoggerMessage] (Logging.g.cs) or [GeneratedRegex] (RegexGenerator.g.cs).
        // Emitting every Location surfaces the same logical type 3+ times. Canonicalize
        // on the symbol's original definition (handles constructed generics) and, by
        // default, emit only the user-authored partial(s). Opt-in restores the raw list.
        foreach (var impl in implementations)
        {
            var sourceLocations = impl.Locations.Where(l => l.IsInSource).ToList();
            if (sourceLocations.Count == 0)
            {
                continue;
            }

            IEnumerable<Location> emitted;
            if (includeGeneratedPartials)
            {
                emitted = sourceLocations;
            }
            else
            {
                var userAuthored = sourceLocations
                    .Where(l => !IsGeneratedSourceLocation(l, solution))
                    .ToList();
                // If every declaration is generated (rare — a type that exists only in
                // generated output), still emit one location so the caller isn't told the
                // implementation is empty.
                emitted = userAuthored.Count > 0
                    ? userAuthored
                    : sourceLocations.Take(1);
            }

            foreach (var location in emitted)
            {
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
                results.Add(SymbolMapper.ToLocationDto(location, impl, preview));
            }
        }

        return OrderLocations(results);
    }

    private static bool IsGeneratedSourceLocation(Location location, Solution solution)
    {
        var tree = location.SourceTree;
        if (tree is null)
        {
            return false;
        }

        // Roslyn-emitted source-generator documents are not returned by Solution.GetDocument
        // (they live under GetSourceGeneratedDocumentAsync). Treat a null Document here as a
        // strong signal the tree is generator-produced.
        if (solution.GetDocument(tree) is null)
        {
            return true;
        }

        // Fallback: some generators (or build-time T4/MSBuild outputs) show up in the regular
        // document list but their file path still carries the conventional .g.cs /
        // .generated.cs suffix. Honour that convention — every generator SDK this repo
        // currently encounters (LoggerMessage, GeneratedRegex, StronglyTypedId, etc.) emits
        // one of these two extensions.
        var path = tree.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        return path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<SymbolDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("ReferenceService.FindOverridesAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        // find-overrides-virtual-declaration-site-doc: NetworkDocumentation audit
        // (20260408T195220Z) and ITChatBot I3 showed that invoking find_overrides at an
        // OVERRIDE site (e.g., `Device.Equals`) returned an empty list while invoking at
        // the virtual declaration returned the real override chain. Roslyn's
        // SymbolFinder.FindOverridesAsync walks DOWN from the current symbol, so starting
        // from a leaf override walks further-derived overrides only. Promote to the
        // original virtual/interface declaration before the walk so both caret positions
        // yield the same (complete) result set.
        var promoted = PromoteToVirtualRoot(symbol);

        // find-overrides-payload-overflow-on-corlib-virtual (gh #754): once promotion lands on
        // a corlib virtual (System.Object.ToString / Equals / GetHashCode, IDisposable.Dispose,
        // etc.), SymbolFinder.FindOverridesAsync enumerates every concrete override across the
        // solution — potentially hundreds of unrelated types, producing an unbounded payload.
        // Mirror the suppression pattern in SymbolRelationshipService.GetSymbolRelationshipsAsync
        // (gh #757): return an empty list immediately when the promoted symbol's containing type
        // is a builtin SpecialType. This is defense-in-depth — the find_overrides wrapper also
        // emits an explanatory hint at the tool layer. member_hierarchy.overrides (which routes
        // through this method) silently benefits from the same guard.
        if (IsCorlibVirtualMember(promoted))
        {
            return [];
        }

        // member-hierarchy-overrides-mislabels-sibling-interface-impls (gh #736, gh #737):
        // FindOverridesAsync now returns ONLY true virtual/abstract overrides — symbols
        // actually marked `override` of a virtual/abstract declaration. Sibling interface
        // implementations (e.g., independent IDisposable.Dispose impls across a solution)
        // moved to FindSiblingInterfaceImplementationsAsync; consumers that want both
        // buckets must call both methods (see member_hierarchy).
        var overrides = await SymbolFinder.FindOverridesAsync(promoted, solution, cancellationToken: ct).ConfigureAwait(false);

        // find-base-members-vs-member-hierarchy-metadata-drift: return SymbolDto instead of
        // LocationDto so metadata-boundary members (e.g. IEquatable<T>.Equals implementations
        // whose base sits in corlib) are not silently dropped by an IsInSource filter. Aligns
        // with member_hierarchy, which already maps base members/overrides through SymbolMapper.ToDto.
        return overrides
            .Distinct(SymbolEqualityComparer.Default)
            .Select(overrideSymbol => SymbolMapper.ToDto(overrideSymbol, solution))
            .OrderBy(d => d.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.StartLine ?? int.MaxValue)
            .ThenBy(d => d.StartColumn ?? int.MaxValue)
            .ThenBy(d => d.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<SymbolDto>> FindSiblingInterfaceImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("ReferenceService.FindSiblingInterfaceImplementationsAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        // Mirror the override-site promotion so a caret on an implementing method
        // (e.g., `Dog.Speak`) resolves to the same interface root as a caret on the
        // interface declaration (`IAnimal.Speak`) and produces the same sibling set.
        var promoted = PromoteToVirtualRoot(symbol);

        var siblings = await FindInterfaceMemberImplementationsAsync(promoted, solution, ct).ConfigureAwait(false);

        return siblings
            .Distinct(SymbolEqualityComparer.Default)
            .Select(sibling => SymbolMapper.ToDto(sibling, solution))
            .OrderBy(d => d.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.StartLine ?? int.MaxValue)
            .ThenBy(d => d.StartColumn ?? int.MaxValue)
            .ThenBy(d => d.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<IReadOnlyList<ISymbol>> FindInterfaceMemberImplementationsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        if (!IsInterfaceMember(symbol))
        {
            return [];
        }

        var results = new List<ISymbol>();
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
                foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(typeDeclaration, ct) is not INamedTypeSymbol type ||
                        type.TypeKind == TypeKind.Interface)
                    {
                        continue;
                    }

                    var implementation = type.FindImplementationForInterfaceMember(symbol);
                    if (implementation is not null)
                    {
                        results.Add(implementation);
                    }
                }
            }
        }

        return results;
    }

    private static bool IsInterfaceMember(ISymbol symbol)
    {
        return symbol.ContainingType?.TypeKind == TypeKind.Interface
            && symbol is IMethodSymbol or IPropertySymbol or IEventSymbol;
    }

    private static IReadOnlyList<LocationDto> OrderLocations(IReadOnlyList<LocationDto> dtos) =>
        dtos
            .OrderBy(d => d.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();

    /// <summary>
    /// Walks an override or interface-implementation symbol to its virtual/interface root so
    /// that <see cref="SymbolFinder.FindOverridesAsync"/> sees the full override chain. Returns
    /// the input symbol unchanged if it is not itself an override.
    /// </summary>
    /// <remarks>
    /// Exposed for the <c>find_overrides</c> tool wrapper so it can detect corlib-virtual
    /// suppression (gh #754) and emit an explanatory hint without re-deriving the promotion
    /// logic. Service-internal callers in this class also use it before invoking
    /// <see cref="SymbolFinder.FindOverridesAsync"/>.
    /// </remarks>
    public static ISymbol PromoteToVirtualRoot(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol { IsOverride: true } method:
            {
                var current = method;
                while (current.OverriddenMethod is { } baseMethod)
                {
                    current = baseMethod;
                }
                return current;
            }
            case IPropertySymbol { IsOverride: true } property:
            {
                var current = property;
                while (current.OverriddenProperty is { } baseProperty)
                {
                    current = baseProperty;
                }
                return current;
            }
            case IEventSymbol { IsOverride: true } ev:
            {
                var current = ev;
                while (current.OverriddenEvent is { } baseEvent)
                {
                    current = baseEvent;
                }
                return current;
            }
            case IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } m:
                return m.ExplicitInterfaceImplementations[0];
            case IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } p:
                return p.ExplicitInterfaceImplementations[0];
            case IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 } e:
                return e.ExplicitInterfaceImplementations[0];
            case IMethodSymbol method:
            {
                var iface = TryFindImplicitlyImplementedInterfaceMember(method);
                return iface ?? symbol;
            }
            case IPropertySymbol prop:
            {
                var iface = TryFindImplicitlyImplementedInterfaceMember(prop);
                return iface ?? symbol;
            }
            case IEventSymbol evt:
            {
                var iface = TryFindImplicitlyImplementedInterfaceMember(evt);
                return iface ?? symbol;
            }
            default:
                return symbol;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="symbol"/> is a member (method/property/event) whose
    /// containing type is a builtin corlib <see cref="SpecialType"/> — i.e.
    /// <c>System.Object.ToString</c> / <c>Equals</c> / <c>GetHashCode</c>, <c>System.IDisposable.Dispose</c>,
    /// etc. Used to short-circuit <see cref="SymbolFinder.FindOverridesAsync"/> on the corlib path
    /// (gh #754), where the unbounded enumeration would otherwise return every concrete override
    /// across the solution and overflow the MCP response cap.
    /// </summary>
    /// <remarks>
    /// Pass the post-<see cref="PromoteToVirtualRoot"/> symbol — promotion is what walks an
    /// override site (e.g. <c>MyClass.ToString</c>) up to the corlib virtual root the guard
    /// is meant to suppress.
    /// </remarks>
    public static bool IsCorlibVirtualMember(ISymbol symbol)
    {
        if (symbol is not (IMethodSymbol or IPropertySymbol or IEventSymbol))
        {
            return false;
        }

        return symbol.ContainingType is { SpecialType: not SpecialType.None };
    }

    /// <summary>
    /// Returns true when <paramref name="symbol"/> is a corlib/BCL interface or abstract-class
    /// ROOT defined entirely in metadata — e.g. <c>System.IDisposable</c>, <c>System.IComparable</c>,
    /// <c>System.Collections.Generic.IEnumerable&lt;T&gt;</c>. These are the implementation roots
    /// whose solution-wide enumeration via <see cref="SymbolFinder.FindImplementationsAsync"/> is
    /// unreliable when anchored by metadata name: <c>Compilation.GetTypeByMetadataName</c> binds
    /// the symbol to one project's corlib reference, so cross-project implementers that bind a
    /// different corlib symbol identity silently drop out — the metadataName query can read as
    /// "no implementers" (count 0) while a source-anchored query at a usage site returns the full
    /// set (find-implementations-corlib-metadataname-zero). The <c>find_implementations</c> tool
    /// wrapper uses this to emit a source-anchor hint instead of a bare (possibly-empty) set.
    /// </summary>
    /// <remarks>
    /// Same metadata-boundary family as the <c>find_overrides</c> corlib-virtual guard
    /// (<see cref="IsCorlibVirtualMember"/>, gh #754) and goto_type_definition #607/#623. This
    /// predicate is intentionally TYPE-scoped (interface/abstract root) rather than member-scoped.
    /// </remarks>
    public static bool IsCorlibImplementationRoot(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol type)
        {
            return false;
        }

        // Only interfaces and abstract classes are "implementation roots" find_implementations
        // walks down from. Concrete/sealed corlib types have no implementers to enumerate.
        if (type.TypeKind != TypeKind.Interface && !type.IsAbstract)
        {
            return false;
        }

        // Must be a metadata (framework) type — a user-declared interface/abstract class has
        // source declarations and enumerates reliably, so the hint must NOT fire on it.
        if (!type.Locations.IsDefaultOrEmpty && type.Locations.Any(static l => l.IsInSource))
        {
            return false;
        }

        return IsCorlibAssembly(type.ContainingAssembly, type.SpecialType);
    }

    /// <summary>
    /// Heuristic for "framework/corlib assembly" — the assemblies whose interface/type roots the
    /// metadata-name <see cref="SymbolFinder.FindImplementationsAsync"/> path enumerates
    /// unreliably. The primary signal is a recognized <see cref="SpecialType"/> on the type
    /// (mirrors the <see cref="IsCorlibVirtualMember"/> gate): the C# compiler only stamps a
    /// non-<see cref="SpecialType.None"/> value on the canonical runtime corlib types
    /// (<c>System.IDisposable</c>, <c>System.IComparable</c>, <c>System.Collections.IEnumerable</c>,
    /// etc.), so this is precise and never over-matches a third-party assembly. The caller
    /// (<see cref="IsCorlibImplementationRoot"/>) resolves <see cref="SpecialType"/> from the type.
    /// </summary>
    /// <remarks>
    /// The <c>StartsWith("System.")</c> branch is kept ONLY as a DOCUMENTED secondary fallback for
    /// BCL types that carry no <see cref="SpecialType"/> (e.g. generic-interface roots like
    /// <c>System.Collections.Generic.IEnumerable&lt;T&gt;</c> on some target frameworks). It is
    /// gated behind the explicit BCL-assembly allowlist plus the corlib name set so a third-party
    /// <c>System.MyCompany.Foo</c> assembly with no <see cref="SpecialType"/> is NOT misclassified
    /// as a corlib implementation root (the over-match this method previously had). Per the
    /// acceptance criterion the <c>StartsWith</c> fallback is permitted as a secondary signal.
    /// </remarks>
    private static bool IsCorlibAssembly(IAssemblySymbol? assembly) => IsCorlibAssembly(assembly, SpecialType.None);

    private static bool IsCorlibAssembly(IAssemblySymbol? assembly, SpecialType specialType)
    {
        if (assembly is null)
        {
            return false;
        }

        // Primary corlib signal: a recognized SpecialType is stamped only on canonical runtime
        // corlib types, so it is unambiguous and cannot be forged by a third-party System.* name.
        if (specialType != SpecialType.None)
        {
            return true;
        }

        var name = assembly.Identity.Name;

        // Explicit corlib / BCL-assembly allowlist — the runtime corlib plus the BCL forwarding
        // assemblies whose interface roots enumerate unreliably via metadata name.
        if (name is "System.Private.CoreLib" or "mscorlib" or "netstandard" or "System.Runtime")
        {
            return true;
        }

        // Documented secondary fallback (acceptance-criterion-permitted): a System.*-prefixed
        // assembly with NO SpecialType. Retained for BCL generic-interface roots that some target
        // frameworks leave without a SpecialType. This is the branch that previously over-matched
        // third-party System.* assemblies; it now only runs after the SpecialType primary gate and
        // the allowlist have both declined, which is acceptable per the row's acceptance criterion.
        return name.StartsWith("System.", StringComparison.Ordinal);
    }

    private static IMethodSymbol? TryFindImplicitlyImplementedInterfaceMember(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        if (containing is null || containing.TypeKind == TypeKind.Interface)
            return null;

        foreach (var iface in containing.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var impl = containing.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, method))
                    return member;
            }
        }

        return null;
    }

    private static IPropertySymbol? TryFindImplicitlyImplementedInterfaceMember(IPropertySymbol property)
    {
        var containing = property.ContainingType;
        if (containing is null || containing.TypeKind == TypeKind.Interface)
            return null;

        foreach (var iface in containing.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IPropertySymbol>())
            {
                var impl = containing.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, property))
                    return member;
            }
        }

        return null;
    }

    private static IEventSymbol? TryFindImplicitlyImplementedInterfaceMember(IEventSymbol ev)
    {
        var containing = ev.ContainingType;
        if (containing is null || containing.TypeKind == TypeKind.Interface)
            return null;

        foreach (var iface in containing.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IEventSymbol>())
            {
                var impl = containing.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, ev))
                    return member;
            }
        }

        return null;
    }

    public Task<IReadOnlyList<SymbolDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        _logger.LogDebug("ReferenceService.FindBaseMembersAsync: workspaceId={WorkspaceId} locator={Locator}", workspaceId, locator);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        return ResolveBaseMembersAsync(solution, locator, ct);
    }

    private static async Task<IReadOnlyList<SymbolDto>> ResolveBaseMembersAsync(Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        // find-base-members-vs-member-hierarchy-metadata-drift: return SymbolDto instead of
        // LocationDto so metadata-boundary base members (e.g. IEquatable<T>.Equals from corlib)
        // are not silently dropped by an IsInSource filter. Aligns with member_hierarchy, which
        // already maps base members through SymbolMapper.ToDto.
        return SymbolServiceHelpers.GetBaseMembers(symbol)
            .Distinct(SymbolEqualityComparer.Default)
            .Select(baseMember => SymbolMapper.ToDto(baseMember, solution))
            .OrderBy(d => d.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.StartLine ?? int.MaxValue)
            .ThenBy(d => d.StartColumn ?? int.MaxValue)
            .ThenBy(d => d.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct)
    {
        _logger.LogDebug("ReferenceService.FindReferencesBulkAsync: workspaceId={WorkspaceId} symbolCount={SymbolCount} includeDefinition={IncludeDefinition}", workspaceId, symbols.Count, includeDefinition);
        if (symbols.Count > 50)
            throw new ArgumentException("Maximum of 50 symbols per bulk request.");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Outer fan-out: how many bulk symbols we resolve concurrently. Each task may itself
        // spawn parallel preview fetches inside ReferenceLocationMaterializer, so keep the outer
        // cap modest on big boxes to avoid runaway document-cache pressure.
        var bulkParallelism = Math.Clamp(Environment.ProcessorCount / 2, 4, 8);
        using var semaphore = new SemaphoreSlim(bulkParallelism, bulkParallelism);

        async Task<BulkReferenceResultDto> ProcessOneAsync(BulkSymbolLocator bulk, int index)
        {
            var key = bulk.SymbolHandle ?? bulk.MetadataName
                ?? (bulk.FilePath is not null && bulk.Line.HasValue && bulk.Column.HasValue
                    ? $"{bulk.FilePath}:{bulk.Line}:{bulk.Column}"
                    : $"symbol[{index}]");

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var locator = ToSymbolLocator(bulk);
                var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
                if (symbol is null)
                    return new BulkReferenceResultDto(key, null, 0, [], "Symbol could not be resolved.");

                var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                var locations = new List<LocationDto>();

                if (includeDefinition)
                {
                    foreach (var loc in symbol.Locations.Where(l => l.IsInSource))
                    {
                        var doc = solution.GetDocument(loc.SourceTree!);
                        var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, loc, ct).ConfigureAwait(false) : null;
                        locations.Add(SymbolMapper.ToLocationDto(loc, symbol, preview, "Definition"));
                    }
                }

                var refLocations = references.SelectMany(r => r.Locations).ToList();
                var refDtos = await ReferenceLocationMaterializer.MaterializeDtosAsync(refLocations, ct).ConfigureAwait(false);
                locations.AddRange(refDtos);

                return new BulkReferenceResultDto(key, symbol.ToDisplayString(), locations.Count, locations, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new BulkReferenceResultDto(key, null, 0, [], ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var tasks = symbols.Select((s, i) => ProcessOneAsync(s, i)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static SymbolLocator ToSymbolLocator(BulkSymbolLocator bulk)
    {
        if (!string.IsNullOrWhiteSpace(bulk.SymbolHandle))
            return SymbolLocator.ByHandle(bulk.SymbolHandle);
        if (!string.IsNullOrWhiteSpace(bulk.MetadataName))
            return SymbolLocator.ByMetadataName(bulk.MetadataName);
        if (!string.IsNullOrWhiteSpace(bulk.FilePath) && bulk.Line.HasValue && bulk.Column.HasValue)
            return SymbolLocator.BySource(bulk.FilePath, bulk.Line.Value, bulk.Column.Value);

        // find-references-bulk-schema-error-ux: FirewallAnalyzer audit §14 showed callers
        // constructing bulk payloads with the wrong shape (missing `symbols` array or empty
        // locator objects). Surface a concrete JSON example so the first-try-fails cycle
        // becomes a first-try-succeeds cycle.
        throw new ArgumentException(
            "BulkSymbolLocator requires at least one of: symbolHandle, metadataName, or filePath+line+column (all four coordinates required for source lookup). " +
            "Common mistakes: using the wrong tool parameter name (must be `symbols` as an array of objects, not `symbolHandles`); " +
            "passing a flat string array instead of locator objects; omitting line/column when using filePath. " +
            "Example payload: { \"symbols\": [" +
            "{ \"metadataName\": \"MyNamespace.MyType\" }, " +
            "{ \"symbolHandle\": \"<base64 handle from enclosing_symbol or document_symbols>\" }, " +
            "{ \"filePath\": \"C:/src/Foo.cs\", \"line\": 42, \"column\": 7 }" +
            "] }. " +
            "Every entry must populate exactly ONE of the three locator strategies.");
    }
}
