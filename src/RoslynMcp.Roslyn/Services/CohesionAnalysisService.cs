using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CohesionAnalysisService : ICohesionAnalysisService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CohesionAnalysisService> _logger;

    public CohesionAnalysisService(IWorkspaceManager workspace, ILogger<CohesionAnalysisService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CohesionMetricsDto>> GetCohesionMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minMethods, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<CohesionMetricsDto>();

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
            var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);
            documents = projects.SelectMany(p => p.Documents);
        }

        foreach (var doc in documents)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (semanticModel is null || root is null) continue;

            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                try
                {
                    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
                    if (typeSymbol is null) continue;

                    // Skip interfaces — LCOM4 is trivially equal to method count and not meaningful
                    if (typeSymbol.TypeKind == TypeKind.Interface) continue;

                    var instanceMethods = typeSymbol.GetMembers()
                        .OfType<IMethodSymbol>()
                        .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && !m.IsImplicitlyDeclared)
                        .ToList();

                    if (minMethods.HasValue && instanceMethods.Count < minMethods.Value) continue;
                    if (instanceMethods.Count < 2) continue; // LCOM only meaningful with 2+ methods

                    var instanceFields = typeSymbol.GetMembers()
                        .Where(m => (m is IFieldSymbol f && !f.IsStatic && !f.IsImplicitlyDeclared) ||
                                    (m is IPropertySymbol p && !p.IsStatic && !p.IsImplicitlyDeclared))
                        .ToList();

                    // Build method → fields accessed map
                    var methodFieldMap = new Dictionary<string, HashSet<string>>();
                    foreach (var method in instanceMethods)
                    {
                        var accessed = FindAccessedFields(method, typeSymbol, typeDecl, semanticModel, ct);
                        methodFieldMap[method.Name] = accessed;
                    }

                    // Compute connected components (LCOM4)
                    var clusters = ComputeClusters(methodFieldMap);

                    var loc = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource);
                    var lineSpan = loc?.GetLineSpan();

                    results.Add(new CohesionMetricsDto(
                        TypeName: typeSymbol.Name,
                        FullyQualifiedName: typeSymbol.ToDisplayString(),
                        FilePath: lineSpan?.Path,
                        Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
                        MethodCount: instanceMethods.Count,
                        FieldCount: instanceFields.Count,
                        Lcom4Score: clusters.Count,
                        Clusters: clusters));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to analyze cohesion for type '{TypeName}' in {File}, skipping",
                        typeDecl.Identifier.Text, doc.FilePath);
                }
            }
        }

        return results.OrderByDescending(r => r.Lcom4Score).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<SharedMemberDto>> FindSharedMembersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol) return [];

        var typeDecl = await FindTypeDeclarationAsync(typeSymbol, solution, ct).ConfigureAwait(false);
        if (typeDecl is null) return [];

        var doc = solution.GetDocument(typeDecl.SyntaxTree);
        if (doc is null) return [];
        var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return [];

        var publicMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic &&
                        m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToList();

        var privateMembers = typeSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && !m.IsStatic &&
                        m.DeclaredAccessibility == Accessibility.Private &&
                        m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IFieldSymbol or IPropertySymbol)
            .ToList();

        // For each private member, find which public methods reference it
        var results = new List<SharedMemberDto>();
        foreach (var privateMember in privateMembers)
        {
            var callers = new List<string>();
            foreach (var publicMethod in publicMethods)
            {
                if (MethodAccessesMember(publicMethod, privateMember, typeDecl, semanticModel, ct))
                {
                    callers.Add(publicMethod.Name);
                }
            }

            if (callers.Count >= 2)
            {
                var loc = privateMember.Locations.FirstOrDefault(l => l.IsInSource);
                var lineSpan = loc?.GetLineSpan();
                results.Add(new SharedMemberDto(
                    MemberName: privateMember.Name,
                    Kind: privateMember.Kind.ToString(),
                    FilePath: lineSpan?.Path,
                    Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
                    CallingMethods: callers.OrderBy(c => c).ToList()));
            }
        }

        return results.OrderByDescending(r => r.CallingMethods.Count).ToList();
    }

    private static HashSet<string> FindAccessedFields(
        IMethodSymbol method, INamedTypeSymbol containingType,
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, CancellationToken ct)
    {
        var accessed = new HashSet<string>(StringComparer.Ordinal);
        var methodLocation = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (methodLocation?.SourceTree is null) return accessed;

        var root = methodLocation.SourceTree.GetRoot(ct);
        var methodNode = root.FindNode(methodLocation.SourceSpan);

        foreach (var identifier in methodNode.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(identifier, ct);
            var referencedSymbol = symbolInfo.Symbol;
            if (referencedSymbol is null) continue;

            if (referencedSymbol is IFieldSymbol field &&
                SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType) &&
                !field.IsStatic)
            {
                accessed.Add(field.Name);
            }
            else if (referencedSymbol is IPropertySymbol prop &&
                     SymbolEqualityComparer.Default.Equals(prop.ContainingType, containingType) &&
                     !prop.IsStatic)
            {
                accessed.Add(prop.Name);
            }
            else if (referencedSymbol is IMethodSymbol calledMethod &&
                     SymbolEqualityComparer.Default.Equals(calledMethod.ContainingType, containingType) &&
                     !calledMethod.IsStatic &&
                     calledMethod.DeclaredAccessibility == Accessibility.Private &&
                     calledMethod.MethodKind == MethodKind.Ordinary)
            {
                // Private method calls create transitive coupling
                accessed.Add(calledMethod.Name);
            }
        }

        return accessed;
    }

    private static bool MethodAccessesMember(
        IMethodSymbol method, ISymbol member,
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, CancellationToken ct)
    {
        var methodLocation = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (methodLocation?.SourceTree is null) return false;

        var root = methodLocation.SourceTree.GetRoot(ct);
        var methodNode = root.FindNode(methodLocation.SourceSpan);

        foreach (var identifier in methodNode.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(identifier, ct);
            if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, member))
                return true;
        }

        return false;
    }

    private static List<MethodClusterDto> ComputeClusters(Dictionary<string, HashSet<string>> methodFieldMap)
    {
        // Build adjacency: two methods are connected if they share any field/private-method access
        var methodNames = methodFieldMap.Keys.ToList();
        var parent = new Dictionary<string, string>();
        foreach (var name in methodNames)
            parent[name] = name;

        string Find(string x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        // Union methods that share at least one field
        for (int i = 0; i < methodNames.Count; i++)
        {
            for (int j = i + 1; j < methodNames.Count; j++)
            {
                if (methodFieldMap[methodNames[i]].Overlaps(methodFieldMap[methodNames[j]]))
                {
                    Union(methodNames[i], methodNames[j]);
                }
            }
        }

        // Group by root
        var groups = methodNames.GroupBy(Find).ToList();

        return groups.Select(g =>
        {
            var methods = g.OrderBy(m => m).ToList();
            var sharedFields = methods
                .SelectMany(m => methodFieldMap[m])
                .Distinct()
                .OrderBy(f => f)
                .ToList();
            return new MethodClusterDto(methods, sharedFields);
        }).ToList();
    }

    private static async Task<TypeDeclarationSyntax?> FindTypeDeclarationAsync(
        INamedTypeSymbol typeSymbol, Solution solution, CancellationToken ct)
    {
        var location = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null) return null;

        var root = await location.SourceTree.GetRootAsync(ct).ConfigureAwait(false);
        var node = root.FindNode(location.SourceSpan);
        return node as TypeDeclarationSyntax ?? node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
    }
}
