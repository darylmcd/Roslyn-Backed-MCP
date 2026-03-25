using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Services;

public sealed class AdvancedAnalysisService : IAdvancedAnalysisService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<AdvancedAnalysisService> _logger;

    public AdvancedAnalysisService(IWorkspaceManager workspace, ILogger<AdvancedAnalysisService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId, string? projectFilter, bool includePublic, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<UnusedSymbolDto>();

        var projects = FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>();
                foreach (var decl in declarations)
                {
                    if (ct.IsCancellationRequested || results.Count >= limit) break;

                    var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                    if (symbol is null) continue;
                    if (symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;

                    // Skip public symbols unless explicitly requested
                    if (!includePublic && symbol.DeclaredAccessibility == Accessibility.Public) continue;

                    // Skip constructors, operators, finalizers
                    if (symbol is IMethodSymbol method &&
                        (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                            or MethodKind.Destructor or MethodKind.UserDefinedOperator
                            or MethodKind.Conversion))
                        continue;

                    // Skip interface implementations
                    if (symbol.ContainingType is not null &&
                        symbol.ContainingType.AllInterfaces.Any(i =>
                            i.GetMembers().Any(m => SymbolEqualityComparer.Default.Equals(
                                symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol))))
                        continue;

                    // Skip overrides
                    if (symbol is IMethodSymbol { IsOverride: true } or IPropertySymbol { IsOverride: true })
                        continue;

                    // Skip entry points
                    if (symbol is IMethodSymbol { Name: "Main" } && symbol.IsStatic) continue;

                    var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                    var refCount = refs.Sum(r => r.Locations.Count());

                    if (refCount == 0)
                    {
                        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                        if (location is null) continue;
                        var lineSpan = location.GetLineSpan();

                        results.Add(new UnusedSymbolDto(
                            symbol.Name,
                            symbol.Kind.ToString(),
                            lineSpan.Path,
                            lineSpan.StartLinePosition.Line + 1,
                            lineSpan.StartLinePosition.Character + 1,
                            symbol.ContainingType?.Name,
                            SymbolHandleSerializer.CreateHandle(symbol)));
                    }
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<DiRegistrationDto>();

        var projects = FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

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
                        implType = serviceType;
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

    public async Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<ComplexityMetricsDto>();

        IEnumerable<Document> documents;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalizedPath = Path.GetFullPath(filePath);
            documents = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath is not null && Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var projects = FilterProjects(solution, projectFilter);
            documents = projects.SelectMany(p => p.Documents);
        }

        foreach (var doc in documents)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var tree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (tree is null || semanticModel is null) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            var methodDeclarations = root.DescendantNodes()
                .Where(n => n is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);

            foreach (var decl in methodDeclarations)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                if (symbol is null) continue;

                var complexity = CalculateCyclomaticComplexity(decl);
                if (minComplexity.HasValue && complexity < minComplexity.Value) continue;

                var loc = CalculateLinesOfCode(decl);
                var nesting = CalculateMaxNestingDepth(decl);
                var paramCount = symbol is IMethodSymbol m ? m.Parameters.Length : 0;

                var lineSpan = decl.GetLocation().GetLineSpan();

                results.Add(new ComplexityMetricsDto(
                    symbol.Name,
                    symbol.Kind.ToString(),
                    lineSpan.Path,
                    lineSpan.StartLinePosition.Line + 1,
                    complexity,
                    loc,
                    nesting,
                    paramCount,
                    symbol.ContainingType?.Name));
            }
        }

        return results.OrderByDescending(r => r.CyclomaticComplexity).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<ReflectionUsageDto>();
        var projects = FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                    if (symbolInfo.Symbol is not IMethodSymbol method) continue;

                    var containingNs = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
                    var containingTypeName = method.ContainingType?.Name ?? "";

                    var usageKind = (containingNs, containingTypeName, method.Name) switch
                    {
                        ("System", "Type", "GetType") => "Type.GetType",
                        ("System", "Activator", "CreateInstance") => "Activator.CreateInstance",
                        ("System.Reflection", "Assembly", "GetType") => "Assembly.GetType",
                        ("System.Reflection", "Assembly", "Load") => "Assembly.Load",
                        ("System.Reflection", "Assembly", "LoadFrom") => "Assembly.LoadFrom",
                        ("System.Reflection", _, "Invoke") => "Reflection.Invoke",
                        ("System.Reflection", "PropertyInfo", "GetValue") => "PropertyInfo.GetValue",
                        ("System.Reflection", "PropertyInfo", "SetValue") => "PropertyInfo.SetValue",
                        ("System.Reflection", "FieldInfo", "GetValue") => "FieldInfo.GetValue",
                        ("System.Reflection", "FieldInfo", "SetValue") => "FieldInfo.SetValue",
                        (_, "Type", "GetMethod" or "GetProperty" or "GetField" or "GetMember" or "GetMethods"
                            or "GetProperties" or "GetFields" or "GetMembers") => $"Type.{method.Name}",
                        _ => null
                    };

                    if (usageKind is null) continue;

                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    var containingMethod = GetContainingMethodName(invocation, semanticModel, ct);
                    var typeArg = method.TypeArguments.Length > 0
                        ? method.TypeArguments[0].ToDisplayString()
                        : null;

                    results.Add(new ReflectionUsageDto(
                        usageKind,
                        method.ToDisplayString(),
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1,
                        containingMethod,
                        typeArg));
                }

                // Also check for typeof() expressions
                var typeofExprs = root.DescendantNodes().OfType<TypeOfExpressionSyntax>();
                foreach (var typeofExpr in typeofExprs)
                {
                    var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type, ct);
                    var lineSpan = typeofExpr.GetLocation().GetLineSpan();
                    var containingMethod = GetContainingMethodName(typeofExpr, semanticModel, ct);

                    results.Add(new ReflectionUsageDto(
                        "typeof",
                        "typeof()",
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1,
                        containingMethod,
                        typeInfo.Type?.ToDisplayString()));
                }
            }
        }

        return results;
    }

    public async Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = FilterProjects(solution, projectFilter);

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

                // Count types in this namespace
                var typeCount = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count();
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

        var nodes = namespaceCounts.Select(kvp =>
            new NamespaceNodeDto(kvp.Key, kvp.Value.Count, kvp.Value.Project)).ToList();

        var edgeList = edges.Select(kvp =>
            new NamespaceEdgeDto(kvp.Key.From, kvp.Key.To, kvp.Value)).ToList();

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

    public async Task<IReadOnlyList<SemanticSearchResultDto>> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SemanticSearchResultDto>();
        var projects = FilterProjects(solution, projectFilter);

        // Parse the query into predicates
        var predicates = ParseSemanticQuery(query);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>();
                foreach (var decl in declarations)
                {
                    if (ct.IsCancellationRequested || results.Count >= limit) break;

                    var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                    if (symbol is null || symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;

                    if (!predicates.All(p => p(symbol))) continue;

                    var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location is null) continue;
                    var lineSpan = location.GetLineSpan();

                    results.Add(new SemanticSearchResultDto(
                        symbol.Name,
                        symbol.Kind.ToString(),
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        symbol.ToDisplayString(),
                        symbol.ContainingType?.Name,
                        symbol.ContainingNamespace?.ToDisplayString(),
                        SymbolHandleSerializer.CreateHandle(symbol)));
                }
            }
        }

        return results;
    }

    // --- Helper methods ---

    private static List<Func<ISymbol, bool>> ParseSemanticQuery(string query)
    {
        var predicates = new List<Func<ISymbol, bool>>();
        var q = query.ToLowerInvariant().Trim();

        // Pattern: "async methods" or "async"
        if (q.Contains("async"))
            predicates.Add(s => s is IMethodSymbol m && m.IsAsync);

        // Pattern: "returning Task<bool>" or "returns Task<"
        if (q.Contains("returning ") || q.Contains("returns "))
        {
            var returnTypeStart = q.IndexOf("returning ", StringComparison.Ordinal);
            if (returnTypeStart < 0) returnTypeStart = q.IndexOf("returns ", StringComparison.Ordinal);
            if (returnTypeStart >= 0)
            {
                var afterKeyword = q[(q.IndexOf(' ', returnTypeStart) + 1)..].Trim();
                // Take until next space or end
                var returnType = afterKeyword.Split(' ', ',')[0].Trim();
                predicates.Add(s => s is IMethodSymbol m &&
                    m.ReturnType.ToDisplayString().Contains(returnType, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Pattern: "implementing IDisposable"
        if (q.Contains("implementing "))
        {
            var ifaceName = q[(q.IndexOf("implementing ", StringComparison.Ordinal) + 13)..].Trim().Split(' ', ',')[0];
            predicates.Add(s => s is INamedTypeSymbol t &&
                t.AllInterfaces.Any(i => i.Name.Equals(ifaceName, StringComparison.OrdinalIgnoreCase) ||
                    i.ToDisplayString().Contains(ifaceName, StringComparison.OrdinalIgnoreCase)));
        }

        // Pattern: "abstract methods" or "abstract classes"
        if (q.Contains("abstract"))
            predicates.Add(s => s.IsAbstract);

        // Pattern: "static methods" or "static"
        if (q.Contains("static") && !q.Contains("non-static"))
            predicates.Add(s => s.IsStatic);

        // Pattern: "virtual"
        if (q.Contains("virtual"))
            predicates.Add(s => s.IsVirtual);

        // Pattern: "methods" (filter to methods only)
        if (q.Contains("method"))
            predicates.Add(s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary });

        // Pattern: "classes"
        if (q.Contains("class") && !q.Contains("classes implementing"))
            predicates.Add(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class });

        // Pattern: "interfaces"
        if (q.Contains("interface"))
            predicates.Add(s => s is INamedTypeSymbol { TypeKind: TypeKind.Interface });

        // Pattern: "properties"
        if (q.Contains("propert"))
            predicates.Add(s => s is IPropertySymbol);

        // Pattern: "fields"
        if (q.Contains("field"))
            predicates.Add(s => s is IFieldSymbol);

        // Pattern: "more than N parameters" or "> N parameters"
        var paramMatch = System.Text.RegularExpressions.Regex.Match(q, @"(?:more than|>)\s*(\d+)\s*param");
        if (paramMatch.Success && int.TryParse(paramMatch.Groups[1].Value, out var minParams))
            predicates.Add(s => s is IMethodSymbol m && m.Parameters.Length > minParams);

        // Pattern: "public" / "private" / "internal" / "protected"
        if (q.Contains("public") && !q.Contains("non-public"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Public);
        else if (q.Contains("private"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Private);
        else if (q.Contains("internal"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Internal);
        else if (q.Contains("protected"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Protected);

        // Pattern: "sealed"
        if (q.Contains("sealed"))
            predicates.Add(s => s.IsSealed);

        // Pattern: "generic"
        if (q.Contains("generic"))
            predicates.Add(s => s is INamedTypeSymbol { IsGenericType: true } or IMethodSymbol { IsGenericMethod: true });

        // If no predicates matched, do a name-based search as fallback
        if (predicates.Count == 0)
            predicates.Add(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        return predicates;
    }

    private static int CalculateCyclomaticComplexity(SyntaxNode node)
    {
        var complexity = 1; // Base complexity
        foreach (var child in node.DescendantNodes())
        {
            complexity += child switch
            {
                IfStatementSyntax => 1,
                ElseClauseSyntax { Statement: not IfStatementSyntax } => 0, // else-if counted by if
                CaseSwitchLabelSyntax => 1,
                CasePatternSwitchLabelSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                ConditionalExpressionSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                DoStatementSyntax => 1,
                CatchClauseSyntax => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0
            };
        }
        return complexity;
    }

    private static int CalculateLinesOfCode(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static int CalculateMaxNestingDepth(SyntaxNode node)
    {
        var maxDepth = 0;
        CalculateNestingDepthRecursive(node, 0, ref maxDepth);
        return maxDepth;
    }

    private static void CalculateNestingDepthRecursive(SyntaxNode node, int currentDepth, ref int maxDepth)
    {
        foreach (var child in node.ChildNodes())
        {
            var newDepth = child switch
            {
                BlockSyntax when child.Parent is IfStatementSyntax or ElseClauseSyntax
                    or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                    or DoStatementSyntax or TryStatementSyntax or CatchClauseSyntax
                    or LockStatementSyntax or UsingStatementSyntax => currentDepth + 1,
                _ => currentDepth
            };

            if (newDepth > maxDepth) maxDepth = newDepth;
            CalculateNestingDepthRecursive(child, newDepth, ref maxDepth);
        }
    }

    private static string? GetContainingMethodName(SyntaxNode node, SemanticModel model, CancellationToken ct)
    {
        var ancestor = node.Ancestors().FirstOrDefault(a =>
            a is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);
        if (ancestor is null) return null;
        var symbol = model.GetDeclaredSymbol(ancestor, ct);
        return symbol?.ToDisplayString();
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

    private static IEnumerable<Project> FilterProjects(Solution solution, string? projectFilter)
    {
        return projectFilter is null
            ? solution.Projects
            : solution.Projects.Where(p =>
                string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase));
    }
}
