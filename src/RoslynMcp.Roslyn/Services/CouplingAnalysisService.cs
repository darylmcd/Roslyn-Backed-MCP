using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Computes Robert C. Martin's afferent / efferent coupling and instability for each type in
/// the workspace. Afferent coupling (Ca) is derived from <see cref="SymbolFinder"/> reference
/// walks so we stay consistent with <c>find_references</c> / <c>find_consumers</c>; efferent
/// coupling (Ce) is computed from a syntax+semantic pass over the type's own declaration trees.
/// </summary>
public sealed class CouplingAnalysisService : ICouplingAnalysisService
{
    private const int MaxFailureWarnings = 10;

    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<CouplingAnalysisService> _logger;
    private readonly Func<INamedTypeSymbol, Exception?>? _failureInjector;

    public CouplingAnalysisService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ILogger<CouplingAnalysisService> logger)
        : this(workspace, compilationCache, logger, failureInjector: null)
    {
    }

    internal CouplingAnalysisService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ILogger<CouplingAnalysisService> logger,
        Func<INamedTypeSymbol, Exception?>? failureInjector)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
        _failureInjector = failureInjector;
    }

    public async Task<IReadOnlyList<CouplingMetricsDto>> GetCouplingMetricsAsync(
        string workspaceId,
        string? projectFilter,
        int limit,
        bool excludeTestProjects,
        bool includeInterfaces,
        CancellationToken ct)
    {
        var result = await GetCouplingMetricsResultAsync(
            workspaceId,
            projectFilter,
            limit,
            excludeTestProjects,
            includeInterfaces,
            ct).ConfigureAwait(false);
        return result.Metrics;
    }

    public async Task<CouplingAnalysisResultDto> GetCouplingMetricsResultAsync(
        string workspaceId,
        string? projectFilter,
        int limit,
        bool excludeTestProjects,
        bool includeInterfaces,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter)
            .Where(p => !excludeTestProjects || !ProjectMetadataParser.IsTestProject(p))
            .ToList();
        var candidates = await CollectCandidatesAsync(
            workspaceId,
            projects,
            includeInterfaces,
            ct).ConfigureAwait(false);
        var computation = await ComputeCandidateMetricsAsync(
            workspaceId,
            solution,
            candidates,
            ct).ConfigureAwait(false);

        return BuildResult(computation, limit);
    }

    private async Task<IReadOnlyList<CouplingCandidate>> CollectCandidatesAsync(
        string workspaceId,
        IEnumerable<Project> projects,
        bool includeInterfaces,
        CancellationToken ct)
    {
        var candidates = new List<CouplingCandidate>();
        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (var symbol in RoslynSymbolTraversal.EnumerateNamedTypes(compilation.Assembly.GlobalNamespace))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldAnalyze(symbol, includeInterfaces))
                {
                    candidates.Add(new CouplingCandidate(symbol, project, compilation));
                }
            }
        }

        return candidates;
    }

    private async Task<CouplingComputation> ComputeCandidateMetricsAsync(
        string workspaceId,
        Solution solution,
        IReadOnlyCollection<CouplingCandidate> candidates,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<CouplingMetricsDto>();
        var failures = new ConcurrentBag<string>();
        var maxDop = Math.Max(1, Math.Min(Environment.ProcessorCount, candidates.Count));
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = maxDop,
        };

        await Parallel.ForEachAsync(candidates, parallelOptions, async (candidate, token) =>
        {
            try
            {
                if (_failureInjector?.Invoke(candidate.Type) is { } injectedFailure)
                {
                    throw injectedFailure;
                }

                var metrics = await ComputeMetricsAsync(
                    candidate.Type,
                    workspaceId,
                    candidate.Project,
                    candidate.Compilation,
                    solution,
                    token).ConfigureAwait(false);
                results.Add(metrics);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var typeName = candidate.Type.ToDisplayString();
                failures.Add($"{typeName}: {ex.GetType().Name}");
                _logger.LogWarning(
                    ex,
                    "Failed to compute coupling metrics for type '{TypeName}', skipping",
                    typeName);
            }
        }).ConfigureAwait(false);

        return new CouplingComputation(results, failures);
    }

    private static CouplingAnalysisResultDto BuildResult(CouplingComputation computation, int limit)
    {
        var orderedResults = computation.Results
            .OrderByDescending(r => r.Instability)
            .ThenByDescending(r => r.EfferentCoupling)
            .ThenBy(r => r.FullyQualifiedName, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        var warnings = computation.Failures
            .OrderBy(static failure => failure, StringComparer.Ordinal)
            .Take(MaxFailureWarnings)
            .ToList();

        return new CouplingAnalysisResultDto(
            orderedResults,
            computation.Failures.Count,
            warnings);
    }

    private static bool ShouldAnalyze(INamedTypeSymbol type, bool includeInterfaces)
    {
        // Only types declared in source in this compilation.
        if (!type.Locations.Any(l => l.IsInSource)) return false;

        // Skip compiler-generated types (anonymous types, display classes, etc.).
        if (type.IsImplicitlyDeclared) return false;

        return type.TypeKind switch
        {
            TypeKind.Class or TypeKind.Struct => true,
            TypeKind.Interface => includeInterfaces,
            _ => false,
        };
    }

    private async Task<CouplingMetricsDto> ComputeMetricsAsync(
        INamedTypeSymbol type, string workspaceId, Project project, Compilation compilation, Solution solution, CancellationToken ct)
    {
        var afferent = await ComputeAfferentCouplingAsync(type, workspaceId, _compilationCache, solution, ct).ConfigureAwait(false);
        var efferent = await ComputeEfferentCouplingAsync(type, compilation, ct).ConfigureAwait(false);

        var instability = ComputeInstability(afferent, efferent);
        var classification = Classify(afferent, efferent, instability);

        var sourceLoc = type.Locations.FirstOrDefault(l => l.IsInSource);
        var lineSpan = sourceLoc?.GetLineSpan();

        return new CouplingMetricsDto(
            TypeName: type.Name,
            FullyQualifiedName: type.ToDisplayString(),
            FilePath: lineSpan?.Path,
            Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
            ProjectName: project.Name,
            AfferentCoupling: afferent,
            EfferentCoupling: efferent,
            Instability: instability,
            Classification: classification)
        {
            TypeKind = type.TypeKind.ToString(),
        };
    }

    /// <summary>
    /// Counts DISTINCT external types that reference the target type. "External" = containing
    /// type of the reference is not the same named type (partial declarations collapse into
    /// one entity via <see cref="SymbolEqualityComparer"/>).
    /// </summary>
    private static async Task<int> ComputeAfferentCouplingAsync(
        INamedTypeSymbol type, string workspaceId, ICompilationCache compilationCache, Solution solution, CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(type, solution, ct).ConfigureAwait(false);
        var externalConsumers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var referenced in references)
        {
            foreach (var loc in referenced.Locations)
            {
                if (ct.IsCancellationRequested) break;

                var doc = loc.Document;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                // Route the semantic model through the shared ICompilationCache instead of
                // doc.GetSemanticModelAsync (a raw Roslyn Document fetch). Under GC pressure
                // Roslyn's per-Project compilation memoization (a ConditionalWeakTable) is
                // evictable, so the raw path could redundantly rebuild the referencing project's
                // compilation on every location; the cache holds a strong reference and hands back
                // the same warm Compilation the rest of this class already uses. Mirrors the
                // cached-compilation + SyntaxTrees.Contains guard shape in
                // ComputeEfferentCouplingAsync below.
                var compilation = await compilationCache.GetCompilationAsync(workspaceId, doc.Project, ct).ConfigureAwait(false);
                if (compilation is null) continue;

                var tree = root.SyntaxTree;
                if (!compilation.SyntaxTrees.Contains(tree)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);

                var node = root.FindNode(loc.Location.SourceSpan);
                var containing = RoslynSymbolTraversal.FindContainingType(node, semanticModel, ct);
                if (containing is null) continue;

                if (SymbolEqualityComparer.Default.Equals(containing, type)) continue;
                externalConsumers.Add(containing);
            }
        }

        return externalConsumers.Count;
    }

    /// <summary>
    /// Counts DISTINCT external types that THIS type references. Walks every identifier /
    /// member-access / type reference inside every declaration (partials included) and
    /// aggregates the distinct outbound named-type symbols via <see cref="SymbolEqualityComparer"/>.
    /// Built-in primitive types from the BCL (<c>System</c> / <c>System.Collections.Generic</c>)
    /// are excluded — counting them would drown the signal for every type.
    /// </summary>
    private static async Task<int> ComputeEfferentCouplingAsync(
        INamedTypeSymbol type, Compilation compilation, CancellationToken ct)
    {
        var outbound = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (ct.IsCancellationRequested) break;

            var syntax = await reference.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (syntax is not TypeDeclarationSyntax typeDecl) continue;

            var tree = syntax.SyntaxTree;
            // The tree must belong to this compilation (the partials of a type always live in the
            // same project, which is the compilation we were passed). Guard against a divergent
            // snapshot just in case.
            if (!compilation.SyntaxTrees.Contains(tree)) continue;

            var semanticModel = compilation.GetSemanticModel(tree);
            AddReferencedTypes(outbound, type, typeDecl, semanticModel, ct);
            AddDeclaredBaseTypes(outbound, type, typeDecl, semanticModel, ct);
        }

        return outbound.Count;
    }

    private static void AddReferencedTypes(
        ISet<INamedTypeSymbol> outbound,
        INamedTypeSymbol self,
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        foreach (var descendant in declaration.DescendantNodes())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(descendant, ct);
            var referenced = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            var namedType = referenced is null ? null : ExtractNamedType(referenced);
            if (namedType is not null && IsCountableEfferent(namedType, self))
            {
                outbound.Add(namedType);
            }
        }
    }

    private static void AddDeclaredBaseTypes(
        ISet<INamedTypeSymbol> outbound,
        INamedTypeSymbol self,
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        if (semanticModel.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol declaredSymbol)
        {
            return;
        }

        if (declaredSymbol.BaseType is { } baseType && IsCountableEfferent(baseType, self))
        {
            outbound.Add(baseType);
        }

        foreach (var @interface in declaredSymbol.Interfaces)
        {
            if (IsCountableEfferent(@interface, self))
            {
                outbound.Add(@interface);
            }
        }
    }

    private static INamedTypeSymbol? ExtractNamedType(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol named => UnwrapConstructed(named),
        IMethodSymbol method => UnwrapConstructed(method.ContainingType),
        IFieldSymbol field => UnwrapConstructed(field.ContainingType),
        IPropertySymbol prop => UnwrapConstructed(prop.ContainingType),
        IEventSymbol evt => UnwrapConstructed(evt.ContainingType),
        _ => null,
    };

    private static INamedTypeSymbol? UnwrapConstructed(INamedTypeSymbol? type)
    {
        if (type is null) return null;
        // Map constructed generics (List<Foo>) back to their definition (List<T>) so every
        // instantiation of the same generic counts as one outbound edge, not N.
        return type.IsGenericType ? type.OriginalDefinition : type;
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is a real outbound dependency of
    /// <paramref name="self"/> — i.e. a distinct, source-declared type that isn't a primitive.
    /// </summary>
    private static bool IsCountableEfferent(INamedTypeSymbol candidate, INamedTypeSymbol self)
    {
        if (candidate.IsImplicitlyDeclared) return false;
        if (candidate.SpecialType != SpecialType.None) return false;

        // Skip self (including nested-type-on-self chains).
        if (SymbolEqualityComparer.Default.Equals(candidate, self)) return false;

        // Skip nested types owned by self — they are part of the same outer type.
        var outer = candidate.ContainingType;
        while (outer is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(outer, self)) return false;
            outer = outer.ContainingType;
        }

        // Only count types that have at least one source location. Pure metadata references
        // (BCL, NuGet packages) are noise for coupling — Martin's metric is about module
        // boundaries within the SUT, not transitive library usage.
        return candidate.Locations.Any(l => l.IsInSource);
    }

    private static double ComputeInstability(int afferent, int efferent)
    {
        var total = afferent + efferent;
        if (total == 0) return 0.0;
        return (double)efferent / total;
    }

    private static string Classify(int afferent, int efferent, double instability)
    {
        if (afferent == 0 && efferent == 0) return "isolated";
        if (instability < 0.3) return "stable";
        if (instability > 0.7) return "unstable";
        return "balanced";
    }

    private sealed record CouplingCandidate(
        INamedTypeSymbol Type,
        Project Project,
        Compilation Compilation);

    private sealed record CouplingComputation(
        ConcurrentBag<CouplingMetricsDto> Results,
        ConcurrentBag<string> Failures);
}
