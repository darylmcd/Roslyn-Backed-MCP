using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Scans a workspace for dependency-injection registration patterns and reports the
/// service / implementation type pairs along with their declared lifetime. Split out of
/// the legacy <c>DependencyAnalysisService</c> as part of the SRP refactor.
/// </summary>
public sealed class DiRegistrationService : IDiRegistrationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<DiRegistrationService> _logger;

    /// <summary>
    /// di-registrations-scan-caching: full scan is ~12 s on Jellyfin (40 projects, 187
    /// registrations). Cache scan results per (workspaceId, version, projectFilter) so repeat
    /// callers don't re-walk every syntax tree. Invalidated on workspace close via
    /// <see cref="IWorkspaceManager.WorkspaceClosed"/> and on workspace version bump.
    /// </summary>
    /// <remarks>
    /// di-lifetime-mismatch-detection: each snapshot holds the WIDER raw list (including
    /// <c>TryAdd*</c> entries) plus a memoized legacy view (<c>Add*</c>-only) and lazily-built
    /// override-chains. The Top10 regression test
    /// <c>GetDiRegistrations_RepeatCallSameVersion_ReturnsCachedReference</c> asserts reference
    /// equality on repeat calls — memoizing the legacy view in the snapshot preserves that.
    /// </remarks>
    private readonly ConcurrentDictionary<string, DiCacheEntry> _scanCache = new(StringComparer.Ordinal);

    private sealed record DiCacheEntry(int Version, ConcurrentDictionary<string, ScanSnapshot> ByFilter);

    /// <summary>
    /// Per-filter cache snapshot. <see cref="Raw"/> is the full scan (Add* + TryAdd*).
    /// <see cref="LegacyView"/> is the <c>Add*</c>-only projection returned by
    /// <see cref="GetDiRegistrationsAsync"/> — cached so repeat callers get the same reference.
    /// <see cref="OverrideChains"/> is lazily computed on first override-mode call and then
    /// memoized.
    /// </summary>
    private sealed class ScanSnapshot
    {
        private IReadOnlyList<DiRegistrationOverrideChainDto>? _overrideChains;

        public ScanSnapshot(IReadOnlyList<DiRegistrationDto> raw, IReadOnlyList<DiRegistrationDto> legacyView)
        {
            Raw = raw;
            LegacyView = legacyView;
        }

        public IReadOnlyList<DiRegistrationDto> Raw { get; }

        public IReadOnlyList<DiRegistrationDto> LegacyView { get; }

        public IReadOnlyList<DiRegistrationOverrideChainDto> GetOrBuildOverrideChains()
        {
            // Volatile single-writer read/write — worst case is two threads each compute once,
            // both write the same deterministic result, and the last writer wins.
            return _overrideChains ??= BuildOverrideChains(Raw);
        }
    }

    private const string UnfilteredKey = "<all>";
    private const int MaxFilterEntriesPerWorkspace = 8;

    public DiRegistrationService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ILogger<DiRegistrationService> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
        _workspace.WorkspaceClosed += workspaceId => _scanCache.TryRemove(workspaceId, out _);
        // Item #7: also drop scan cache on reload so DI registrations reflect the new solution.
        _workspace.WorkspaceReloaded += workspaceId => _scanCache.TryRemove(workspaceId, out _);
    }

    public async Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var snapshot = await GetOrLoadSnapshotAsync(workspaceId, projectFilter, ct).ConfigureAwait(false);
        return snapshot.LegacyView;
    }

    public async Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var snapshot = await GetOrLoadSnapshotAsync(workspaceId, projectFilter, ct).ConfigureAwait(false);
        return new DiRegistrationScanResult(snapshot.LegacyView, snapshot.GetOrBuildOverrideChains());
    }

    private async Task<ScanSnapshot> GetOrLoadSnapshotAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        // di-registrations-scan-caching: serve from cache if present and the workspace hasn't
        // bumped its version. Cap entries per workspace to avoid unbounded growth on chatty
        // filter combinations.
        var version = _workspace.GetCurrentVersion(workspaceId);
        var key = projectFilter ?? UnfilteredKey;

        var entry = _scanCache.AddOrUpdate(
            workspaceId,
            _ => new DiCacheEntry(version, new ConcurrentDictionary<string, ScanSnapshot>(StringComparer.OrdinalIgnoreCase)),
            (_, existing) => existing.Version == version
                ? existing
                : new DiCacheEntry(version, new ConcurrentDictionary<string, ScanSnapshot>(StringComparer.OrdinalIgnoreCase)));

        if (entry.ByFilter.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var raw = await ScanProjectsAsync(workspaceId, solution, projectFilter, ct).ConfigureAwait(false);
        // di-lifetime-mismatch-detection: derive the legacy view once at cache-population time
        // so every repeat call returns the same reference (preserves the Top10V2Regression
        // GetDiRegistrations_RepeatCallSameVersion_ReturnsCachedReference contract).
        var legacyView = raw.Count == 0
            ? (IReadOnlyList<DiRegistrationDto>)Array.Empty<DiRegistrationDto>()
            : raw.Where(r => !IsTryAddMethod(r.RegistrationMethod)).ToList();
        var snapshot = new ScanSnapshot(raw, legacyView);

        if (entry.ByFilter.Count >= MaxFilterEntriesPerWorkspace)
        {
            var someKey = entry.ByFilter.Keys.FirstOrDefault();
            if (someKey is not null) entry.ByFilter.TryRemove(someKey, out _);
        }
        entry.ByFilter[key] = snapshot;
        return snapshot;
    }

    private async Task<IReadOnlyList<DiRegistrationDto>> ScanProjectsAsync(
        string workspaceId, Solution solution, string? projectFilter, CancellationToken ct)
    {
        var results = new List<DiRegistrationDto>();
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                SemanticModel semanticModel;
                SyntaxNode root;
                try
                {
                    semanticModel = compilation.GetSemanticModel(tree);
                    root = await tree.GetRootAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to analyze syntax tree {Path} for DI registrations, skipping",
                        tree.FilePath);
                    continue;
                }

                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    if (TryCreateDiRegistration(invocation, semanticModel, ct, out var dto))
                        results.Add(dto);
                }
            }
        }

        return results;
    }

    private static bool TryCreateDiRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct,
        out DiRegistrationDto dto)
    {
        dto = default!;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return false;

        var receiverType = method.ReceiverType ?? method.Parameters.FirstOrDefault()?.Type;
        if (receiverType is null)
            return false;

        var isServiceCollectionMethod = receiverType.Name is "IServiceCollection"
            || receiverType.AllInterfaces.Any(i => i.Name == "IServiceCollection");

        if (!isServiceCollectionMethod && !method.ContainingType.Name.Contains("ServiceCollection", StringComparison.Ordinal))
            return false;

        var lifetime = MapDiLifetime(method.Name);
        if (lifetime is null)
            return false;

        var serviceType = "unknown";
        var implType = "unknown";

        if (method.TypeArguments.Length == 2)
        {
            serviceType = method.TypeArguments[0].ToDisplayString();
            implType = method.TypeArguments[1].ToDisplayString();
        }
        else if (method.TypeArguments.Length == 1)
        {
            serviceType = method.TypeArguments[0].ToDisplayString();
            var args = invocation.ArgumentList.Arguments;
            if (args.Count > 0 &&
                args[0].Expression is AnonymousFunctionExpressionSyntax or LambdaExpressionSyntax)
            {
                implType = "factory";
            }
            else if (args.Count > 0)
            {
                // FLAG-11B: When the argument is an instance (not a lambda factory), the
                // implementation is whatever runtime object the caller passes. Try to resolve
                // the argument's compile-time type via the semantic model and report that;
                // fall back to "instance" rather than mirroring the service type.
                implType = ResolveInstanceArgumentType(args[0].Expression, semanticModel, ct) ?? "instance";
            }
            else
            {
                implType = serviceType;
            }
        }

        if (serviceType == "unknown" &&
            TryGetDiTypesFromTypeOfArguments(invocation, semanticModel, ct, out var st, out var it))
        {
            serviceType = st;
            implType = it;
        }

        var lineSpan = invocation.GetLocation().GetLineSpan();
        dto = new DiRegistrationDto(
            serviceType,
            implType,
            lifetime,
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            method.Name);
        return true;
    }

    private static string? MapDiLifetime(string methodName) => methodName switch
    {
        "AddSingleton" => "Singleton",
        "AddScoped" => "Scoped",
        "AddTransient" => "Transient",
        "AddHostedService" => "Singleton",
        "AddKeyedSingleton" => "Singleton",
        "AddKeyedScoped" => "Scoped",
        "AddKeyedTransient" => "Transient",
        // di-lifetime-mismatch-detection: include TryAdd* so the override-chain analysis can
        // model first-wins (TryAdd) vs last-wins (Add) semantics. The legacy
        // GetDiRegistrationsAsync path filters these back out to preserve its default shape.
        "TryAddSingleton" => "Singleton",
        "TryAddScoped" => "Scoped",
        "TryAddTransient" => "Transient",
        "TryAddEnumerable" => "Enumerable",
        _ => null
    };

    private static bool IsTryAddMethod(string methodName) => methodName switch
    {
        "TryAddSingleton" => true,
        "TryAddScoped" => true,
        "TryAddTransient" => true,
        "TryAddEnumerable" => true,
        _ => false
    };

    private static bool TryGetDiTypesFromTypeOfArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct,
        out string serviceType,
        out string implType)
    {
        serviceType = "unknown";
        implType = "unknown";
        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 2 &&
            args[0].Expression is TypeOfExpressionSyntax t0 &&
            args[1].Expression is TypeOfExpressionSyntax t1)
        {
            // FLAG-11A: typeof(X) is itself a System.Type expression — the SEMANTIC type of the
            // typeof expression is always System.Type. The actual type being captured lives in
            // the inner TypeSyntax (t0.Type). Resolve the inner type via GetTypeInfo on .Type
            // (or GetSymbolInfo for unbound generics) so we capture e.g. ILogger<>, not Type.
            var st = ResolveTypeOfArgument(t0, semanticModel, ct);
            var it = ResolveTypeOfArgument(t1, semanticModel, ct);
            if (st is not null && it is not null)
            {
                serviceType = st;
                implType = it;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// FLAG-11A: Resolve the inner type of a <c>typeof(X)</c> expression. Handles bound and
    /// unbound generic types (e.g., <c>typeof(ILogger&lt;&gt;)</c>).
    /// </summary>
    private static string? ResolveTypeOfArgument(TypeOfExpressionSyntax typeOf, SemanticModel semanticModel, CancellationToken ct)
    {
        // First try regular type info on the inner type syntax.
        var typeInfo = semanticModel.GetTypeInfo(typeOf.Type, ct);
        if (typeInfo.Type is not null && typeInfo.Type is not IErrorTypeSymbol)
        {
            return typeInfo.Type.ToDisplayString();
        }

        // For unbound generics like typeof(ILogger<>) the type info path may not resolve
        // through GetTypeInfo on the type syntax — fall back to GetSymbolInfo.
        var symbolInfo = semanticModel.GetSymbolInfo(typeOf.Type, ct);
        if (symbolInfo.Symbol is INamedTypeSymbol named)
        {
            return named.IsUnboundGenericType ? named.ToDisplayString() : named.ToDisplayString();
        }

        // Final fallback: return the syntactic text so the caller at least sees what was passed.
        return typeOf.Type.ToString();
    }

    /// <summary>
    /// FLAG-11B: For a one-generic AddSingleton/AddScoped/AddTransient overload that takes a
    /// pre-constructed instance, resolve the compile-time type of the argument expression so
    /// the implementation column is meaningful instead of being a copy of the service type.
    /// </summary>
    private static string? ResolveInstanceArgumentType(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken ct)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, ct);
        if (typeInfo.Type is not null && typeInfo.Type is not IErrorTypeSymbol)
        {
            return typeInfo.Type.ToDisplayString();
        }
        var converted = typeInfo.ConvertedType;
        if (converted is not null && converted is not IErrorTypeSymbol)
        {
            return converted.ToDisplayString();
        }
        return null;
    }

    /// <summary>
    /// di-lifetime-mismatch-detection: project the raw scan into per-service-type override
    /// chains. Models MS.DI's descriptor resolution semantics:
    /// <list type="bullet">
    ///   <item><description>Source order is determined by file path then line number across
    ///   the matched projects. This is a deterministic static approximation of the runtime
    ///   call order; it cannot model conditional branches that swap composition-root entries
    ///   at startup.</description></item>
    ///   <item><description>Each <c>Add*</c> call appends a descriptor; each <c>TryAdd*</c>
    ///   call appends only if no descriptor exists yet for that service type.</description></item>
    ///   <item><description>The "winner" returned by <c>GetService&lt;TService&gt;()</c> is the
    ///   LAST descriptor in the resulting list.</description></item>
    ///   <item><description>Earlier descriptors are <c>overridden</c>; <c>TryAdd*</c> calls
    ///   that did not push a descriptor are <c>shadowed</c>.</description></item>
    ///   <item><description><c>unknown</c> service types (could not resolve generic argument)
    ///   are skipped — they would group spuriously and produce noise.</description></item>
    /// </list>
    /// Service types with only one registration are also skipped from the chain output
    /// (no override, nothing to flag).
    /// </summary>
    private static IReadOnlyList<DiRegistrationOverrideChainDto> BuildOverrideChains(
        IReadOnlyList<DiRegistrationDto> rawRegistrations)
    {
        var chains = new List<DiRegistrationOverrideChainDto>();
        var groups = rawRegistrations
            .Where(r => !string.Equals(r.ServiceType, "unknown", StringComparison.Ordinal))
            .GroupBy(r => r.ServiceType, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(r => r.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(r => r.Line)
                .ToList();

            if (ordered.Count < 2)
            {
                continue;
            }

            var entries = new List<DiRegistrationOverrideEntryDto>(ordered.Count);
            var lifetimesSeen = new HashSet<string>(StringComparer.Ordinal);
            DiRegistrationDto? activeDescriptor = null;
            DiRegistrationDto? winningDescriptor = null;

            foreach (var registration in ordered)
            {
                lifetimesSeen.Add(registration.Lifetime);

                string status;
                if (IsTryAddMethod(registration.RegistrationMethod))
                {
                    if (activeDescriptor is null)
                    {
                        // First registration for this service type — TryAdd takes effect.
                        activeDescriptor = registration;
                        winningDescriptor = registration;
                        status = "winning";
                    }
                    else
                    {
                        // A descriptor already exists; TryAdd is a no-op for GetService.
                        status = "shadowed";
                    }
                }
                else
                {
                    // Unconditional Add*: replaces the prior winner for GetService<T>.
                    activeDescriptor = registration;
                    winningDescriptor = registration;
                    status = "winning";
                }

                entries.Add(new DiRegistrationOverrideEntryDto(
                    registration.ImplementationType,
                    registration.Lifetime,
                    registration.FilePath,
                    registration.Line,
                    registration.RegistrationMethod,
                    status));
            }

            // Mark every prior "winning" entry as overridden — only the last winning entry
            // actually wins. Walk the list once from the end, demoting earlier winners.
            var seenFinalWinner = false;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].EffectiveStatus != "winning")
                {
                    continue;
                }

                if (!seenFinalWinner)
                {
                    seenFinalWinner = true;
                    continue;
                }

                entries[i] = entries[i] with { EffectiveStatus = "overridden" };
            }

            var deadCount = entries.Count(e => e.EffectiveStatus is "overridden" or "shadowed");
            var lifetimesDiffer = lifetimesSeen.Count > 1;
            // winningDescriptor is non-null because Count >= 2 guarantees at least one entry
            // and the loop always promotes the first call to winning (Add or first-TryAdd).
            var winner = winningDescriptor!;

            chains.Add(new DiRegistrationOverrideChainDto(
                group.Key,
                entries,
                winner.Lifetime,
                winner.ImplementationType,
                lifetimesDiffer,
                deadCount));
        }

        // Stable ordering for callers: alphabetical by service type.
        return chains
            .OrderBy(c => c.ServiceType, StringComparer.Ordinal)
            .ToList();
    }
}
