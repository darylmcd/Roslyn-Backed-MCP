using System.Diagnostics;
using System.Text.Json;
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
    private readonly ICompilationCache _compilationCache;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<DependencyAnalysisService> _logger;
    private readonly ValidationServiceOptions _options;

    public DependencyAnalysisService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        IGatedCommandExecutor executor,
        ILogger<DependencyAnalysisService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
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

    public async Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projectDtos = new List<NuGetProjectDto>();
        var packageMap = new Dictionary<(string Id, string Version), List<string>>();
        var packagesPropsPath = MsBuildMetadataHelper.FindDirectoryPackagesProps(_workspace.GetStatus(workspaceId).LoadedPath);

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

                    string? resolvedCentral = null;
                    if (string.Equals(version, "centrally-managed", StringComparison.OrdinalIgnoreCase) &&
                        packagesPropsPath is not null)
                    {
                        resolvedCentral = MsBuildMetadataHelper.TryGetCentralPackageVersion(packagesPropsPath, id);
                    }

                    packages.Add(new NuGetPackageReferenceDto(id, version, resolvedCentral));

                    var key = (id, version);
                    if (!packageMap.TryGetValue(key, out var users))
                    {
                        users = [];
                        packageMap[key] = users;
                    }
                    users.Add(project.Name);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to parse project file {Path}", project.FilePath);
            }

            projectDtos.Add(new NuGetProjectDto(project.Name, project.FilePath, packages));
        }

        var packageDtos = packageMap.Select(kvp =>
        {
            var displayVersion = kvp.Key.Version;
            if (string.Equals(kvp.Key.Version, "centrally-managed", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = projectDtos
                    .SelectMany(p => p.PackageReferences)
                    .Where(pr => string.Equals(pr.PackageId, kvp.Key.Id, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(pr.Version, "centrally-managed", StringComparison.OrdinalIgnoreCase))
                    .Select(pr => pr.ResolvedCentralVersion)
                    .FirstOrDefault(rv => !string.IsNullOrEmpty(rv));
                if (resolved is not null)
                    displayVersion = resolved;
            }

            return new NuGetPackageDto(kvp.Key.Id, displayVersion, kvp.Value);
        }).ToList();

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
        _ => null
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

    public async Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId,
        string? projectFilter,
        bool includeTransitive,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var status = await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(status.LoadedPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        var targetPath = string.IsNullOrWhiteSpace(projectFilter)
            ? status.LoadedPath
            : _executor.ResolveProject(workspaceId, projectFilter).FilePath;

        var args = new List<string> { "list", targetPath, "package", "--vulnerable", "--format", "json" };
        if (includeTransitive)
        {
            args.Add("--include-transitive");
        }

        var execution = await _executor.ExecuteAsync(
            workspaceId,
            targetPath,
            args,
            _options.VulnerabilityScanTimeout,
            ct).ConfigureAwait(false);

        sw.Stop();

        if (!execution.Succeeded)
        {
            var err = string.IsNullOrWhiteSpace(execution.StdErr) ? execution.StdOut : execution.StdErr;
            throw new InvalidOperationException(
                $"dotnet list package --vulnerable failed (exit {execution.ExitCode}). {err.Trim()}");
        }

        var vulnerabilities = ParseNuGetVulnerabilityJson(execution.StdOut, out var scannedProjects);
        var critical = vulnerabilities.Count(v => string.Equals(v.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var high = vulnerabilities.Count(v => string.Equals(v.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var medium = vulnerabilities.Count(v => string.Equals(v.Severity, "Medium", StringComparison.OrdinalIgnoreCase));
        var low = vulnerabilities.Count(v => string.Equals(v.Severity, "Low", StringComparison.OrdinalIgnoreCase));

        return new NuGetVulnerabilityScanResultDto(
            vulnerabilities,
            scannedProjects,
            vulnerabilities.Count,
            critical,
            high,
            medium,
            low,
            includeTransitive,
            sw.ElapsedMilliseconds);
    }

    private static IReadOnlyList<NuGetVulnerablePackageDto> ParseNuGetVulnerabilityJson(string json, out int scannedProjects)
    {
        scannedProjects = 0;
        var results = new List<NuGetVulnerablePackageDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("projects", out var projectsEl) || projectsEl.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        scannedProjects = projectsEl.GetArrayLength();

        foreach (var project in projectsEl.EnumerateArray())
        {
            var projectPath = project.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
            var projectName = string.IsNullOrEmpty(projectPath)
                ? "unknown"
                : Path.GetFileNameWithoutExtension(projectPath);

            if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                AddPackagesFromFramework(framework, "topLevelPackages", isTransitive: false);
                AddPackagesFromFramework(framework, "transitivePackages", isTransitive: true);
            }

            void AddPackagesFromFramework(JsonElement fw, string arrayName, bool isTransitive)
            {
                if (!fw.TryGetProperty(arrayName, out var pkgs) || pkgs.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var pkg in pkgs.EnumerateArray())
                {
                    var id = pkg.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var resolved = pkg.TryGetProperty("resolvedVersion", out var rv)
                        ? rv.GetString() ?? ""
                        : pkg.TryGetProperty("requestedVersion", out var rq)
                            ? rq.GetString() ?? ""
                            : "";

                    if (!pkg.TryGetProperty("vulnerabilities", out var vulns) || vulns.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var vuln in vulns.EnumerateArray())
                    {
                        var severity = vuln.TryGetProperty("severity", out var sev)
                            ? sev.GetString() ?? "Unknown"
                            : "Unknown";
                        var advisoryUrl = vuln.TryGetProperty("advisoryurl", out var adv)
                            ? adv.GetString()
                            : vuln.TryGetProperty("advisoryUrl", out var adv2)
                                ? adv2.GetString()
                                : null;

                        var dedupeKey = $"{projectPath}|{id}|{resolved}|{advisoryUrl}|{severity}|{isTransitive}";
                        if (!seen.Add(dedupeKey))
                        {
                            continue;
                        }

                        results.Add(new NuGetVulnerablePackageDto(
                            id,
                            resolved,
                            severity,
                            advisoryUrl,
                            projectName,
                            isTransitive,
                            PatchedVersion: null));
                    }
                }
            }
        }

        return results;
    }
}
