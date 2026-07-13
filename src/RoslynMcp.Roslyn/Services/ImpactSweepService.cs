using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 9 implementation. Runs three sub-queries in parallel:
/// <list type="number">
///   <item><description>Cross-solution references for the target symbol.</description></item>
///   <item><description>Switch-exhaustiveness diagnostics (CS8509, CS8524, IDE0072) project-wide.</description></item>
///   <item><description>Callsites where the enclosing type name matches a mapper/converter/serializer suffix (heuristic).</description></item>
/// </list>
/// Emits a structured <see cref="SymbolImpactSweepDto"/> so downstream tooling can surface the
/// grouped results as a review checklist without separately hitting each underlying tool.
/// </summary>
public sealed class ImpactSweepService : IImpactSweepService
{
    private static readonly string[] SwitchExhaustivenessIds = ["CS8509", "CS8524", "IDE0072"];
    private static readonly string[] MapperSuffixes = ["Mapper", "Converter", "Serializer", "Translator", "Adapter"];

    private readonly IWorkspaceManager _workspace;
    private readonly IReferenceService _references;
    private readonly IDiagnosticService _diagnostics;
    private readonly ICompilationCache _compilationCache;

    public ImpactSweepService(IWorkspaceManager workspace, IReferenceService references, IDiagnosticService diagnostics, ICompilationCache compilationCache)
    {
        _workspace = workspace;
        _references = references;
        _diagnostics = diagnostics;
        _compilationCache = compilationCache;
    }

    public async Task<SymbolImpactSweepDto> SweepAsync(
        string workspaceId,
        SymbolLocator locator,
        CancellationToken ct,
        bool summary = false,
        int? maxItemsPerCategory = null)
    {
        locator.Validate();

        // Run the three independent queries in parallel. The references query blocks on the
        // workspace read side; diagnostics runs against the analyzer pipeline.
        // Pass `summary` through to FindReferencesAsync so the heavy preview text is
        // dropped at the source — saves materializing it just to discard later.
        var referencesTask = _references.FindReferencesAsync(workspaceId, locator, ct, summary);
        var diagnosticsTask = CollectSwitchExhaustivenessDiagnosticsAsync(workspaceId, ct);

        var references = await referencesTask.ConfigureAwait(false);
        var diagnostics = await diagnosticsTask.ConfigureAwait(false);

        // Mapper callsites are a post-filter over references — only keep those whose file path
        // or enclosing type name contains a mapper suffix.
        var mapperCallsites = FilterMapperCallsites(references);

        // Resolve the symbol once for the response header AND the static-extension-host
        // detection — saves a second SymbolResolver.ResolveAsync round-trip for the hint.
        var resolvedSymbol = await ResolveSymbolAsync(workspaceId, locator, ct).ConfigureAwait(false);
        var symbolDisplay = resolvedSymbol?.ToDisplayString() ?? "<unresolved>";

        // find-references-static-extension-host-blind-spot: when the target is a static class
        // whose only consumption pattern is `obj.ExtensionMethod()` syntax, the type-level
        // reference index returns 0 hits even though every extension call binds to a member
        // of the host. Surface this as an explicit `callers_callees` hint in the suggested
        // tasks so reviewers don't conclude the type is dead.
        var isStaticExtensionHost = resolvedSymbol is INamedTypeSymbol { IsStatic: true } host &&
            host.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod);

        // Item 10: when the swept symbol is a property carrying a JSON/DataMember attribute,
        // surface paired-DTO mapper findings (To*/From* asymmetry).
        var persistenceFindings = await CollectPersistenceLayerFindingsAsync(workspaceId, locator, ct).ConfigureAwait(false);

        // symbol-impact-sweep-suggested-tasks-count-drift: capture pre-cap totals BEFORE
        // truncation so the suggested-tasks string reports the true blast radius (e.g. "Review
        // 193 reference(s)") rather than the truncated list length ("Review 10 reference(s)").
        // The capped lists are what we return to the caller, but reviewers need the unbounded
        // count to size the work.
        var totalReferenceCount = references.Count;
        var totalDiagnosticCount = diagnostics.Count;
        var totalMapperCount = mapperCallsites.Count;

        // symbol-impact-sweep-output-size-blowup: maxItemsPerCategory truncates each list
        // INDEPENDENTLY (so a 1500-ref symbol with 0 mapper callsites still returns the
        // mapper-callsite list). Persistence findings are not capped — they are always
        // small in practice and represent qualitatively distinct review work.
        if (maxItemsPerCategory is int cap && cap >= 0)
        {
            references = references.Take(cap).ToList();
            mapperCallsites = mapperCallsites.Take(cap).ToList();
            diagnostics = diagnostics.Take(cap).ToList();
        }

        var tasks = BuildSuggestedTasks(
            totalReferenceCount, totalDiagnosticCount, totalMapperCount, persistenceFindings.Count, isStaticExtensionHost);

        return new SymbolImpactSweepDto(
            SymbolDisplay: symbolDisplay,
            References: references,
            SwitchExhaustivenessIssues: diagnostics,
            MapperCallsites: mapperCallsites,
            SuggestedTasks: tasks,
            PersistenceLayerFindings: persistenceFindings);
    }

    /// <summary>
    /// Item 10 implementation. If the target symbol is a property:
    /// <list type="number">
    ///   <item><description>Collect serialization attributes on the property (<c>JsonPropertyName</c>, <c>JsonInclude</c>, <c>DataMember</c>).</description></item>
    ///   <item><description>Find sibling DTO types in the solution that contain a property with the same name (or matching <c>JsonPropertyName</c>).</description></item>
    ///   <item><description>For each sibling pair, locate mapper-suffixed types whose methods read or write the property; classify per direction (<c>To*</c> writes, <c>From*</c> reads).</description></item>
    ///   <item><description>Emit a finding when one direction is missing.</description></item>
    /// </list>
    /// Returns an empty list when the target isn't a property or no DTO sibling is found —
    /// the heuristic intentionally stays narrow to keep false-positive rate down.
    /// </summary>
    private async Task<IReadOnlyList<PersistenceLayerFindingDto>> CollectPersistenceLayerFindingsAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not IPropertySymbol property) return Array.Empty<PersistenceLayerFindingDto>();

        var findings = new List<PersistenceLayerFindingDto>();
        var notes = new List<string>();

        // Look for sibling DTO types that mirror this property (same declared name OR a JSON
        // property name match).
        var jsonName = ExtractJsonPropertyName(property);
        var propertyName = property.Name;

        var dtoSiblings = new List<(INamedTypeSymbol DtoType, IPropertySymbol DtoProperty)>();
        foreach (var project in solution.Projects)
        {
            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;
            foreach (var dto in compilation.GlobalNamespace.GetAllTypes(allowedKinds: TypeKind.Class))
            {
                if (SymbolEqualityComparer.Default.Equals(dto, property.ContainingType)) continue;
                if (!IsLikelyDtoType(dto)) continue;
                foreach (var dtoProperty in dto.GetMembers().OfType<IPropertySymbol>())
                {
                    if (string.Equals(dtoProperty.Name, propertyName, StringComparison.Ordinal) ||
                        (jsonName is not null && string.Equals(ExtractJsonPropertyName(dtoProperty), jsonName, StringComparison.Ordinal)))
                    {
                        dtoSiblings.Add((dto, dtoProperty));
                    }
                }
            }
        }

        if (dtoSiblings.Count == 0) return Array.Empty<PersistenceLayerFindingDto>();

        // Find mapper-suffixed types that operate on these DTOs and detect whether they reference
        // the property in serialize (To*) and/or deserialize (From*) directions.
        //
        // The domain type is fixed for the whole call, so hoist the O(solution-types) suffix +
        // domain-name scan OUT of the per-sibling loop: enumerate every project's types once here
        // (a single GetCompilationAsync per project) to build a sibling-independent candidate
        // SUPERSET, then per-sibling narrow it to the same-method domain+dtoType matches in-memory.
        // Previously FindMapperTypesAsync re-walked every project's types per dtoSibling —
        // O(dtoSiblings x solution-types) — recompiling each project once per sibling.
        var mapperCandidates = await CollectMapperCandidateTypesAsync(
            workspaceId, _compilationCache, solution, property.ContainingType, ct).ConfigureAwait(false);

        foreach (var (dtoType, dtoProperty) in dtoSiblings)
        {
            var mappers = FilterMappersForDtoType(mapperCandidates, property.ContainingType, dtoType);
            if (mappers.Count == 0) continue;

            var directionsPresent = new List<string>();
            var directionsMissing = new List<string>();

            foreach (var direction in new[] { "To", "From" })
            {
                var matchingMethod = mappers
                    .SelectMany(m => m.GetMembers().OfType<IMethodSymbol>())
                    .FirstOrDefault(m => m.Name.StartsWith(direction, StringComparison.Ordinal));
                if (matchingMethod is null)
                {
                    directionsMissing.Add(direction);
                    continue;
                }
                if (await MethodReferencesPropertyAsync(matchingMethod, property, dtoProperty, ct).ConfigureAwait(false))
                    directionsPresent.Add(direction);
                else
                    directionsMissing.Add(direction);
            }

            if (directionsMissing.Count > 0)
            {
                findings.Add(new PersistenceLayerFindingDto(
                    PropertyName: propertyName,
                    DomainTypeDisplay: property.ContainingType.ToDisplayString(),
                    DtoTypeDisplay: dtoType.ToDisplayString(),
                    DirectionsPresent: directionsPresent,
                    DirectionsMissing: directionsMissing,
                    Notes: notes));
            }
        }

        return findings;
    }

    private static string? ExtractJsonPropertyName(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is "JsonPropertyNameAttribute" or "DataMemberAttribute" or "JsonPropertyAttribute")
            {
                var arg = attr.ConstructorArguments.FirstOrDefault();
                if (arg.Value is string s && !string.IsNullOrEmpty(s)) return s;
                var named = attr.NamedArguments.FirstOrDefault(n => n.Key is "Name" or "PropertyName");
                if (named.Value.Value is string ns && !string.IsNullOrEmpty(ns)) return ns;
            }
        }
        return null;
    }

    private static bool IsLikelyDtoType(INamedTypeSymbol type)
    {
        var name = type.Name;
        if (type.IsRecord) return true;
        if (name.EndsWith("Dto", StringComparison.Ordinal)) return true;
        if (name.EndsWith("Snapshot", StringComparison.Ordinal)) return true;
        if (name.StartsWith("Snapshot", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// One pass over every project's types (a single cached <c>GetCompilationAsync</c> per project):
    /// keeps mapper-suffixed types that have at least one method whose signature mentions the fixed
    /// <paramref name="domainType"/>. This is a sibling-independent SUPERSET of the per-dtoType
    /// matches — a type can only satisfy the same-method domain+dtoType check in
    /// <see cref="FilterMappersForDtoType"/> if it already has a domain-mentioning method here — so
    /// hoisting it out of the per-dtoSibling loop preserves behaviour while collapsing the scan cost
    /// from O(dtoSiblings x solution-types) to O(solution-types).
    /// </summary>
    /// <remarks><c>internal</c> (not <c>private</c>) so the hoist can be regression-covered directly:
    /// driving it through <see cref="SweepAsync"/> muddies the compilation-cache call count with the
    /// reference/diagnostic sub-queries' own fetches, so the project-count-scaling assertion is only
    /// clean against this helper in isolation. See
    /// <c>CompilationCacheAdoptionTests.ImpactSweepService_MapperCandidateScan_*</c>.</remarks>
    internal static async Task<IReadOnlyList<INamedTypeSymbol>> CollectMapperCandidateTypesAsync(
        string workspaceId, ICompilationCache compilationCache, Solution solution, INamedTypeSymbol domainType, CancellationToken ct)
    {
        var candidates = new List<INamedTypeSymbol>();
        foreach (var project in solution.Projects)
        {
            var compilation = await compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;
            foreach (var type in compilation.GlobalNamespace.GetAllTypes(allowedKinds: TypeKind.Class))
            {
                var name = type.Name;
                if (!(name.EndsWith("Mapper", StringComparison.Ordinal) ||
                      name.EndsWith("Converter", StringComparison.Ordinal) ||
                      name.EndsWith("Serializer", StringComparison.Ordinal) ||
                      name.EndsWith("Translator", StringComparison.Ordinal) ||
                      name.EndsWith("Adapter", StringComparison.Ordinal))) continue;

                var mentionsDomain = type.GetMembers().OfType<IMethodSymbol>().Any(m =>
                    m.ToDisplayString().Contains(domainType.Name, StringComparison.Ordinal));
                if (mentionsDomain) candidates.Add(type);
            }
        }
        return candidates;
    }

    /// <summary>
    /// Per-dtoSibling narrowing of the hoisted <paramref name="candidates"/>: a candidate matches
    /// only when a SINGLE method's signature mentions BOTH the domain type and this
    /// <paramref name="dtoType"/>. Keeping the domain+dtoType check same-method preserves the exact
    /// semantics of the former <c>FindMapperTypesAsync.mentionsBoth</c> predicate — a type with
    /// method A naming the domain and method B naming the dtoType must NOT match. Purely in-memory
    /// over the small candidate list; no compilation fetch or solution-type re-enumeration.
    /// </summary>
    /// <remarks><c>internal</c> so the same-method narrowing semantics can be asserted directly (a
    /// type whose domain-mention and dtoType-mention live in DIFFERENT methods must not match). See
    /// <c>CompilationCacheAdoptionTests.ImpactSweepService_MapperCandidateScan_*</c>.</remarks>
    internal static IReadOnlyList<INamedTypeSymbol> FilterMappersForDtoType(
        IReadOnlyList<INamedTypeSymbol> candidates, INamedTypeSymbol domainType, INamedTypeSymbol dtoType)
    {
        var matches = new List<INamedTypeSymbol>();
        foreach (var type in candidates)
        {
            var mentionsBoth = type.GetMembers().OfType<IMethodSymbol>().Any(m =>
            {
                var sig = m.ToDisplayString();
                return sig.Contains(domainType.Name, StringComparison.Ordinal) &&
                       sig.Contains(dtoType.Name, StringComparison.Ordinal);
            });
            if (mentionsBoth) matches.Add(type);
        }
        return matches;
    }

    private static async Task<bool> MethodReferencesPropertyAsync(
        IMethodSymbol method, IPropertySymbol domainProperty, IPropertySymbol dtoProperty, CancellationToken ct)
    {
        foreach (var declRef in method.DeclaringSyntaxReferences)
        {
            var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
            var text = node.ToFullString();
            if (text.Contains(domainProperty.Name, StringComparison.Ordinal) ||
                text.Contains(dtoProperty.Name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private async Task<IReadOnlyList<DiagnosticDto>> CollectSwitchExhaustivenessDiagnosticsAsync(
        string workspaceId, CancellationToken ct)
    {
        var hits = new List<DiagnosticDto>();
        foreach (var id in SwitchExhaustivenessIds)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _diagnostics
                .GetDiagnosticsAsync(workspaceId, projectFilter: null, fileFilter: null,
                    severityFilter: "Info", diagnosticIdFilter: id, ct)
                .ConfigureAwait(false);

            // We don't distinguish compiler vs analyzer here — a CS8509 is the same concern
            // whether it came from the compiler layer or an IDE analyzer.
            hits.AddRange(result.CompilerDiagnostics);
            hits.AddRange(result.AnalyzerDiagnostics);
        }
        return hits;
    }

    private static IReadOnlyList<LocationDto> FilterMapperCallsites(IReadOnlyList<LocationDto> references)
    {
        if (references.Count == 0) return Array.Empty<LocationDto>();
        var mapperHits = new List<LocationDto>();
        foreach (var loc in references)
        {
            var fileName = Path.GetFileNameWithoutExtension(loc.FilePath);
            if (string.IsNullOrEmpty(fileName)) continue;
            foreach (var suffix in MapperSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    mapperHits.Add(loc);
                    break;
                }
            }
        }
        return mapperHits;
    }

    private async Task<ISymbol?> ResolveSymbolAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        return await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> BuildSuggestedTasks(
        int refCount, int switchCount, int mapperCount, int persistenceCount, bool isStaticExtensionHost)
    {
        var tasks = new List<string>();
        if (refCount > 0)
            tasks.Add($"Review {refCount} reference(s) — update call sites that assume the prior shape.");
        if (switchCount > 0)
            tasks.Add($"Fix {switchCount} non-exhaustive switch statement(s) flagged by CS8509/CS8524/IDE0072.");
        if (mapperCount > 0)
            tasks.Add($"Audit {mapperCount} mapper/converter callsite(s) for paired To*/From* updates.");
        if (persistenceCount > 0)
            tasks.Add($"Resolve {persistenceCount} persistence-layer asymmetry finding(s): a paired DTO mapper omits one direction (To*/From*).");
        if (tasks.Count == 0)
        {
            // find-references-static-extension-host-blind-spot: when the target is a static
            // extension-host class with zero detected impact, the most likely explanation is
            // that every consumer binds via `obj.ExtensionMethod()` syntax — which the
            // type-level reference index does NOT capture. Point reviewers at
            // `callers_callees` per member so they don't conclude the type is unused.
            if (isStaticExtensionHost)
            {
                tasks.Add(
                    "Static extension-host class — type-level reference index is blind to " +
                    "extension-method call sites. Use callers_callees(<MemberName>) on each " +
                    "public member to find consumers.");
            }
            else
            {
                tasks.Add("No impact detected. Consider running project_diagnostics to confirm.");
            }
        }
        return tasks;
    }
}

internal static class NamespaceTypeEnumeration
{
    /// <summary>Recursive walker that yields every named type in a global namespace tree, optionally filtered by kind.</summary>
    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceSymbol root, TypeKind? allowedKinds = null)
    {
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child) stack.Push(child);
                else if (member is INamedTypeSymbol type)
                {
                    if (allowedKinds.HasValue && type.TypeKind != allowedKinds.Value) continue;
                    yield return type;
                    foreach (var nested in type.GetTypeMembers())
                    {
                        if (allowedKinds.HasValue && nested.TypeKind != allowedKinds.Value) continue;
                        yield return nested;
                    }
                }
            }
        }
    }
}
