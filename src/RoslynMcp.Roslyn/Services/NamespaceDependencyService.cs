using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Builds the namespace-dependency graph by walking each project's syntax trees, counting
/// declared types per namespace and recording <c>using</c> directives as edges. Reports
/// circular dependencies via DFS over the resulting adjacency list. Split out of the legacy
/// <c>DependencyAnalysisService</c> as part of the SRP refactor.
/// </summary>
public sealed class NamespaceDependencyService : INamespaceDependencyService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<NamespaceDependencyService> _logger;

    public NamespaceDependencyService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ILogger<NamespaceDependencyService> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
    }

    public async Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        var namespaceCounts = new Dictionary<string, (int Count, string? Project)>();
        var edges = new Dictionary<(string From, string To), int>();

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
                var semanticModel = compilation.GetSemanticModel(tree);

                // Get the file's namespace
                var nsDecl = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                var fileNamespace = nsDecl is not null
                    ? semanticModel.GetDeclaredSymbol(nsDecl, ct)?.ToDisplayString()
                    : null;

                if (fileNamespace is null) continue;

                // Count types in this namespace (classes, records, structs, interfaces, enums, delegates)
                var typeCount = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Count()
                    + root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Count();
                if (namespaceCounts.TryGetValue(fileNamespace, out var existing))
                    namespaceCounts[fileNamespace] = (existing.Count + typeCount, project.Name);
                else
                    namespaceCounts[fileNamespace] = (typeCount, project.Name);

                // Track using directives as edges
                var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                    .Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None) && u.StaticKeyword.IsKind(SyntaxKind.None));

                foreach (var usingDir in usingDirectives)
                {
                    var usedNs = usingDir.Name?.ToString();
                    if (usedNs is null || usedNs == fileNamespace) continue;

                    var key = (fileNamespace, usedNs);
                    edges[key] = edges.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }
        }

        var edgeList = edges.Select(kvp =>
            new NamespaceEdgeDto(kvp.Key.From, kvp.Key.To, kvp.Value)).ToList();

        // Ensure namespaces that appear only as edge targets/sources still appear as nodes (TypeCount may be 0)
        foreach (var edge in edgeList)
        {
            if (!namespaceCounts.ContainsKey(edge.FromNamespace))
                namespaceCounts[edge.FromNamespace] = (0, null);
            if (!namespaceCounts.ContainsKey(edge.ToNamespace))
                namespaceCounts[edge.ToNamespace] = (0, null);
        }

        var nodes = namespaceCounts
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new NamespaceNodeDto(kvp.Key, kvp.Value.Count, kvp.Value.Project))
            .ToList();

        // Detect circular dependencies using DFS
        var cycles = DetectCycles(edgeList);

        return new NamespaceDependencyGraphDto(nodes, edgeList, cycles);
    }

    private static IReadOnlyList<CircularDependencyDto> DetectCycles(IReadOnlyList<NamespaceEdgeDto> edges)
    {
        var adjacency = new Dictionary<string, HashSet<string>>();
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.FromNamespace, out var neighbors))
            {
                neighbors = [];
                adjacency[edge.FromNamespace] = neighbors;
            }
            neighbors.Add(edge.ToNamespace);
        }

        var cycles = new List<CircularDependencyDto>();
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in adjacency.Keys)
        {
            if (!visited.Contains(node))
                DfsCycleDetect(node, adjacency, visited, inStack, path, cycles);
        }

        return cycles;
    }

    private static void DfsCycleDetect(
        string node,
        Dictionary<string, HashSet<string>> adjacency,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        List<CircularDependencyDto> cycles)
    {
        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (inStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = path[cycleStart..].Append(neighbor).ToList();
                        cycles.Add(new CircularDependencyDto(cycle));
                    }
                }
                else if (!visited.Contains(neighbor))
                {
                    DfsCycleDetect(neighbor, adjacency, visited, inStack, path, cycles);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
    }
}
