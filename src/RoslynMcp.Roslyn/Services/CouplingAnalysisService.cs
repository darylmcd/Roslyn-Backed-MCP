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
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CouplingAnalysisService> _logger;

    public CouplingAnalysisService(IWorkspaceManager workspace, ILogger<CouplingAnalysisService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CouplingMetricsDto>> GetCouplingMetricsAsync(
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

        // Enumerate every candidate top-level type across the filtered projects. The tuple carries
        // the compilation so the Ce pass downstream can build semantic models without a second
        // GetCompilationAsync call per type.
        var candidates = new List<(INamedTypeSymbol Type, Project Project, Compilation Compilation)>();
        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var symbol in EnumerateDeclaredTypes(compilation.Assembly.GlobalNamespace))
            {
                if (ct.IsCancellationRequested) break;
                if (!ShouldAnalyze(symbol, includeInterfaces)) continue;
                candidates.Add((symbol, project, compilation));
            }
        }

        var results = new List<CouplingMetricsDto>(candidates.Count);
        foreach (var (type, project, compilation) in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var metrics = await ComputeMetricsAsync(type, project, compilation, solution, ct).ConfigureAwait(false);
                results.Add(metrics);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to compute coupling metrics for type '{TypeName}', skipping",
                    type.ToDisplayString());
            }
        }

        // Order: highest instability first (the "unstable" types most at risk of upstream churn),
        // then Ce desc as a stable tiebreaker so the heavy outgoing-dependency types bubble up.
        return results
            .OrderByDescending(r => r.Instability)
            .ThenByDescending(r => r.EfferentCoupling)
            .ThenBy(r => r.FullyQualifiedName, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateDeclaredTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var t in EnumerateDeclaredTypes(child))
                    yield return t;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                // Nested types count too — they own their own afferent/efferent graph.
                foreach (var nested in EnumerateNestedTypes(type))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNestedTypes(nested))
                yield return deeper;
        }
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
        INamedTypeSymbol type, Project project, Compilation compilation, Solution solution, CancellationToken ct)
    {
        var afferent = await ComputeAfferentCouplingAsync(type, solution, ct).ConfigureAwait(false);
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
        INamedTypeSymbol type, Solution solution, CancellationToken ct)
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
                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (root is null || semanticModel is null) continue;

                var node = root.FindNode(loc.Location.SourceSpan);
                var containing = FindContainingTopLevelType(node, semanticModel, ct);
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

            foreach (var descendant in typeDecl.DescendantNodes())
            {
                if (ct.IsCancellationRequested) break;

                var symbolInfo = semanticModel.GetSymbolInfo(descendant, ct);
                var referenced = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (referenced is null) continue;

                var namedType = ExtractNamedType(referenced);
                if (namedType is null) continue;
                if (!IsCountableEfferent(namedType, type)) continue;

                outbound.Add(namedType);
            }

            // Also include the explicit base type + implemented interfaces. These appear in
            // DescendantNodes as IdentifierNameSyntax nodes too, but pulling them off the
            // semantic model directly guarantees we never miss a base-list entry regardless
            // of how the syntax is shaped (simple identifier vs qualified vs generic).
            if (semanticModel.GetDeclaredSymbol(typeDecl, ct) is INamedTypeSymbol declaredSymbol)
            {
                if (declaredSymbol.BaseType is { } baseType && IsCountableEfferent(baseType, type))
                    outbound.Add(baseType);
                foreach (var iface in declaredSymbol.Interfaces)
                {
                    if (IsCountableEfferent(iface, type))
                        outbound.Add(iface);
                }
            }
        }

        return outbound.Count;
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

    /// <summary>
    /// Walks up the syntax tree from <paramref name="node"/> to find the enclosing top-level
    /// named type (or nested type) and returns its symbol. Returns <c>null</c> when the
    /// reference is outside any type (top-level statements, file-scoped using, etc.).
    /// </summary>
    private static INamedTypeSymbol? FindContainingTopLevelType(
        SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
    {
        var current = node;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
            {
                return semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
            }
            current = current.Parent;
        }
        return null;
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
}
