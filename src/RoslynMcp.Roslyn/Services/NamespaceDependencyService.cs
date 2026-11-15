using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public NamespaceDependencyService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
    }

    public async Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.ResolveProjects(solution, projectFilter);

        var namespaceCounts = new Dictionary<string, (int Count, string? Project)>();
        var edges = new Dictionary<(string From, string To), int>();
        var analyzedProjectCount = 0;

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;
            analyzedProjectCount++;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                if (await tree.GetRootAsync(ct).ConfigureAwait(false) is CompilationUnitSyntax root)
                {
                    RecordTree(root, compilation.GetSemanticModel(tree), project.Name, namespaceCounts, edges, ct);
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

        // Declarations include empty namespace blocks and exclude nodes added only as edge endpoints.
        var declaredNamespaceCount = namespaceCounts.Count(kvp => kvp.Value.Project is not null);

        return new NamespaceDependencyGraphDto(
            nodes,
            edgeList,
            cycles,
            AnalyzedProjectCount: analyzedProjectCount,
            TotalNamespacesScanned: declaredNamespaceCount);
    }

    private static void RecordTree(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string projectName,
        Dictionary<string, (int Count, string? Project)> namespaceCounts,
        Dictionary<(string From, string To), int> edges,
        CancellationToken ct)
    {
        var names = new Dictionary<BaseNamespaceDeclarationSyntax, string>();
        foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(declaration, ct) is { } symbol)
            {
                var name = symbol.ToDisplayString();
                names.Add(declaration, name);
                namespaceCounts[name] = (namespaceCounts.GetValueOrDefault(name).Count, projectName);
            }
        }

        if (names.Count == 0)
            return;

        // A compilation-unit import applies to every named namespace in the file. Reopened
        // declarations of the same namespace share that scope and must count the import once.
        var fileImportOwners = names.Values.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var node in root.DescendantNodes())
        {
            if (NamespaceMemberClassifier.IsTypeLike(node)
                && node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>() is { } owner
                && names.TryGetValue(owner, out var typeNamespace))
            {
                namespaceCounts[typeNamespace] = (namespaceCounts[typeNamespace].Count + 1, projectName);
            }

            if (node is not UsingDirectiveSyntax import
                || !import.GlobalKeyword.IsKind(SyntaxKind.None)
                || !import.StaticKeyword.IsKind(SyntaxKind.None)
                || import.Name is not { } importName)
            {
                continue;
            }

            var target = importName.ToString();
            if (import.Parent is CompilationUnitSyntax)
            {
                foreach (var ownerName in fileImportOwners)
                {
                    RecordImport(edges, ownerName, target);
                }
            }
            else if (import.Parent is BaseNamespaceDeclarationSyntax scope && names.TryGetValue(scope, out var scopeName))
            {
                RecordImport(edges, scopeName, target);
            }
        }
    }

    private static void RecordImport(Dictionary<(string From, string To), int> edges, string owner, string target)
    {
        if (owner != target)
        {
            var edge = (owner, target);
            edges[edge] = edges.GetValueOrDefault(edge) + 1;
        }
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
