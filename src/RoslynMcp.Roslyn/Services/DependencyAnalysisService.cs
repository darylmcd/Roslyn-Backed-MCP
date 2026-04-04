using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Services;

public sealed class DependencyAnalysisService : IDependencyAnalysisService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<DependencyAnalysisService> _logger;

    public DependencyAnalysisService(IWorkspaceManager workspace, ILogger<DependencyAnalysisService> logger)
    {
        _workspace = workspace;
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

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
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

    public async Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projectDtos = new List<NuGetProjectDto>();
        var packageMap = new Dictionary<(string Id, string Version), List<string>>();

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;
            if (project.FilePath is null) continue;

            var packages = new List<NuGetPackageReferenceDto>();

            try
            {
                var doc = XDocument.Load(project.FilePath);
                var packageRefs = doc.Descendants("PackageReference");

                foreach (var pkgRef in packageRefs)
                {
                    var id = pkgRef.Attribute("Include")?.Value;
                    var version = pkgRef.Attribute("Version")?.Value ?? "centrally-managed";
                    if (id is null) continue;

                    packages.Add(new NuGetPackageReferenceDto(id, version));

                    var key = (id, version);
                    if (!packageMap.TryGetValue(key, out var users))
                    {
                        users = [];
                        packageMap[key] = users;
                    }
                    users.Add(project.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse project file {Path}", project.FilePath);
            }

            projectDtos.Add(new NuGetProjectDto(project.Name, project.FilePath, packages));
        }

        var packageDtos = packageMap.Select(kvp =>
            new NuGetPackageDto(kvp.Key.Id, kvp.Key.Version, kvp.Value)).ToList();

        return new NuGetDependencyResultDto(packageDtos, projectDtos);
    }

    public async Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<DiRegistrationDto>();

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
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
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                    if (symbolInfo.Symbol is not IMethodSymbol method) continue;

                    // Check if this is an IServiceCollection extension method
                    var receiverType = method.ReceiverType ?? method.Parameters.FirstOrDefault()?.Type;
                    if (receiverType is null) continue;

                    var isServiceCollectionMethod = receiverType.Name is "IServiceCollection"
                        || receiverType.AllInterfaces.Any(i => i.Name == "IServiceCollection");

                    if (!isServiceCollectionMethod && !method.ContainingType.Name.Contains("ServiceCollection"))
                        continue;

                    var lifetime = method.Name switch
                    {
                        "AddSingleton" => "Singleton",
                        "AddScoped" => "Scoped",
                        "AddTransient" => "Transient",
                        "AddHostedService" => "Singleton",
                        "AddKeyedSingleton" => "Singleton",
                        "AddKeyedScoped" => "Scoped",
                        "AddKeyedTransient" => "Transient",
                        _ => null
                    };

                    if (lifetime is null) continue;

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
                        // Factory / delegate overload: AddSingleton<T>(Func<IServiceProvider, T> factory)
                        var args = invocation.ArgumentList.Arguments;
                        if (args.Count > 0 &&
                            args[0].Expression is AnonymousFunctionExpressionSyntax or LambdaExpressionSyntax)
                        {
                            implType = "factory";
                        }
                        else
                            implType = serviceType;
                    }

                    if (serviceType == "unknown" &&
                        TryGetDiTypesFromTypeOfArguments(invocation, semanticModel, ct, out var st, out var it))
                    {
                        serviceType = st;
                        implType = it;
                    }

                    var lineSpan = invocation.GetLocation().GetLineSpan();

                    results.Add(new DiRegistrationDto(
                        serviceType,
                        implType,
                        lifetime,
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        method.Name));
                }
            }
        }

        return results;
    }

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
            var st = semanticModel.GetTypeInfo(t0, ct).Type;
            var it = semanticModel.GetTypeInfo(t1, ct).Type;
            if (st is not null && it is not null)
            {
                serviceType = st.ToDisplayString();
                implType = it.ToDisplayString();
                return true;
            }
        }

        return false;
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
