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
        string workspaceId, string? filePath, string? projectFilter, int? minMethods, int limit, bool includeInterfaces, bool excludeTestProjects, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<CohesionMetricsDto>();

        IEnumerable<Document> documents;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalizedPath = Path.GetFullPath(filePath);
            documents = solution.Projects
                .Where(p => !excludeTestProjects || !ProjectMetadataParser.IsTestProject(p))
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath is not null && Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter)
                .Where(p => !excludeTestProjects || !ProjectMetadataParser.IsTestProject(p));
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
                    var metrics = AnalyzeTypeCohesion(typeDecl, semanticModel, minMethods, includeInterfaces, ct);
                    if (metrics is not null)
                        results.Add(metrics);
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

    private static CohesionMetricsDto? AnalyzeTypeCohesion(
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, int? minMethods, bool includeInterfaces, CancellationToken ct)
    {
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
        if (typeSymbol is null) return null;

        var sourceLoc = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        var lineSpan = sourceLoc?.GetLineSpan();

        // Handle interfaces — LCOM4 is trivially equal to method count
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            if (!includeInterfaces) return null;
            var methodCount = typeSymbol.GetMembers().OfType<IMethodSymbol>()
                .Count(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared && !IsSourceGenPartial(m));
            if (minMethods.HasValue && methodCount < minMethods.Value) return null;
            return new CohesionMetricsDto(
                TypeName: typeSymbol.Name,
                FullyQualifiedName: typeSymbol.ToDisplayString(),
                FilePath: lineSpan?.Path,
                Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
                MethodCount: methodCount,
                FieldCount: 0,
                Lcom4Score: methodCount,
                Clusters: [])
            { TypeKind = "Interface" };
        }

        // Filter out source-generator partial methods (e.g., [LoggerMessage], [GeneratedRegex])
        // BEFORE clustering. They have no real body to read fields from, so they would each be
        // their own LCOM4 cluster and falsely inflate the score for classes that wrap a single
        // real method around generated logging or regex partials.
        var instanceMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && !m.IsImplicitlyDeclared && !IsSourceGenPartial(m))
            .ToList();

        if (minMethods.HasValue && instanceMethods.Count < minMethods.Value) return null;
        if (instanceMethods.Count < 2) return null;

        var instanceFields = typeSymbol.GetMembers()
            .Where(m => (m is IFieldSymbol f && !f.IsStatic && !f.IsImplicitlyDeclared) ||
                        (m is IPropertySymbol p && !p.IsStatic && !p.IsImplicitlyDeclared))
            .ToList();

        var (methodFieldMap, methodCallMap) = BuildMethodAccessMaps(instanceMethods, typeSymbol, typeDecl, semanticModel, ct);
        var clusters = ComputeClusters(methodFieldMap, methodCallMap);

        return new CohesionMetricsDto(
            TypeName: typeSymbol.Name,
            FullyQualifiedName: typeSymbol.ToDisplayString(),
            FilePath: lineSpan?.Path,
            Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
            MethodCount: instanceMethods.Count,
            FieldCount: instanceFields.Count,
            Lcom4Score: clusters.Count,
            Clusters: clusters)
        { TypeKind = typeSymbol.TypeKind.ToString() };
    }

    /// <summary>
    /// For each method, build two maps:
    /// - <c>methodFieldMap</c>: the field/property names the method accesses (used for SharedFields output).
    /// - <c>methodCallMap</c>: the private helper method names the method calls (used for cluster connectivity).
    /// Splitting these prevents BUG-N9 where a private helper showed up both as a method node AND
    /// inside the SharedFields list of its caller's cluster.
    /// </summary>
    private static (Dictionary<string, HashSet<string>> Fields, Dictionary<string, HashSet<string>> Calls) BuildMethodAccessMaps(
        List<IMethodSymbol> methods, INamedTypeSymbol containingType,
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, CancellationToken ct)
    {
        var fieldMap = new Dictionary<string, HashSet<string>>();
        var callMap = new Dictionary<string, HashSet<string>>();
        foreach (var method in methods)
        {
            var (fields, calls) = FindAccessedMembers(method, containingType, semanticModel, ct);
            fieldMap[method.Name] = fields;
            callMap[method.Name] = calls;
        }
        return (fieldMap, callMap);
    }

    /// <summary>
    /// Returns true when the method is a source-generator partial (no real body) such as
    /// <c>[LoggerMessage]</c> or <c>[GeneratedRegex]</c>. These would otherwise show up as
    /// independent LCOM4 clusters because they don't access any fields, falsely inflating
    /// the score for classes wrapping a single real method around several generated partials.
    /// </summary>
    private static bool IsSourceGenPartial(IMethodSymbol method)
    {
        if (method.IsPartialDefinition) return true;

        // The implementation part may carry the attribute even when the definition does not.
        var implPart = method.PartialImplementationPart;
        var defPart = method.PartialDefinitionPart;
        return HasSourceGenAttribute(method)
            || (implPart is not null && HasSourceGenAttribute(implPart))
            || (defPart is not null && HasSourceGenAttribute(defPart));
    }

    private static bool HasSourceGenAttribute(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name is "Microsoft.Extensions.Logging.LoggerMessageAttribute"
                or "System.Text.RegularExpressions.GeneratedRegexAttribute")
            {
                return true;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<SharedMemberDto>> FindSharedMembersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol) return [];

        var typeDecl = await FindTypeDeclarationAsync(typeSymbol, solution, ct).ConfigureAwait(false);
        if (typeDecl is null) return [];

        // dr-find-shared-members-locator-invalidargument: Build a semantic model map
        // for all partial declarations so MethodAccessesMember can use the correct
        // model for each method's syntax tree (prevents "Syntax node is not within
        // syntax tree" when members span multiple partial files).
        var semanticModelMap = new Dictionary<SyntaxTree, SemanticModel>();
        foreach (var loc in typeSymbol.Locations.Where(l => l.IsInSource && l.SourceTree is not null))
        {
            var tree = loc.SourceTree!;
            if (semanticModelMap.ContainsKey(tree)) continue;
            var doc = solution.GetDocument(tree);
            if (doc is null) continue;
            var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (model is not null)
                semanticModelMap[tree] = model;
        }

        if (semanticModelMap.Count == 0) return [];

        var publicMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && (typeSymbol.IsStatic || !m.IsStatic) &&
                        m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToList();

        var privateMembers = typeSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && (typeSymbol.IsStatic || !m.IsStatic) &&
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
                if (MethodAccessesMember(publicMethod, privateMember, typeDecl, semanticModelMap, ct))
                {
                    callers.Add(publicMethod.Name);
                }
            }

            if (callers.Count >= 2)
            {
                var memberLoc = privateMember.Locations.FirstOrDefault(l => l.IsInSource);
                var lineSpan = memberLoc?.GetLineSpan();
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

    /// <summary>
    /// Walks a method body and returns two disjoint sets:
    /// - <c>fields</c>: instance/static fields and properties on the containing type that the method reads or writes.
    /// - <c>calls</c>: private helper methods on the containing type that the method calls.
    ///
    /// Keeping these separated fixes BUG-N9 — previously private-helper names were merged into
    /// the same set as field names, so the SharedFields output for an LCOM4 cluster ended up
    /// containing helper-method names alongside real fields.
    /// </summary>
    private static (HashSet<string> Fields, HashSet<string> Calls) FindAccessedMembers(
        IMethodSymbol method, INamedTypeSymbol containingType,
        SemanticModel semanticModel, CancellationToken ct)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var calls = new HashSet<string>(StringComparer.Ordinal);
        var methodLocation = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (methodLocation?.SourceTree is null) return (fields, calls);

        var root = methodLocation.SourceTree.GetRoot(ct);
        var methodNode = root.FindNode(methodLocation.SourceSpan);

        foreach (var identifier in methodNode.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(identifier, ct);
            var referencedSymbol = symbolInfo.Symbol;
            if (referencedSymbol is null) continue;

            if (referencedSymbol is IFieldSymbol field &&
                SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType) &&
                (containingType.IsStatic || !field.IsStatic))
            {
                fields.Add(field.Name);
            }
            else if (referencedSymbol is IPropertySymbol prop &&
                     SymbolEqualityComparer.Default.Equals(prop.ContainingType, containingType) &&
                     (containingType.IsStatic || !prop.IsStatic))
            {
                fields.Add(prop.Name);
            }
            else if (referencedSymbol is IMethodSymbol calledMethod &&
                     SymbolEqualityComparer.Default.Equals(calledMethod.ContainingType, containingType) &&
                     (containingType.IsStatic || !calledMethod.IsStatic) &&
                     calledMethod.DeclaredAccessibility == Accessibility.Private &&
                     calledMethod.MethodKind == MethodKind.Ordinary)
            {
                // Private method calls create transitive coupling but should NOT appear as
                // SharedFields. They live in the call map, used only for cluster connectivity.
                calls.Add(calledMethod.Name);
            }
        }

        return (fields, calls);
    }

    private static bool MethodAccessesMember(
        IMethodSymbol method, ISymbol member,
        TypeDeclarationSyntax typeDecl, Dictionary<SyntaxTree, SemanticModel> semanticModelMap, CancellationToken ct)
    {
        var methodLocation = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (methodLocation?.SourceTree is null) return false;

        // dr-find-shared-members-locator-invalidargument: Use the semantic model
        // that matches the method's syntax tree to avoid cross-tree exceptions.
        if (!semanticModelMap.TryGetValue(methodLocation.SourceTree, out var semanticModel))
            return false;

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

    private static List<MethodClusterDto> ComputeClusters(
        Dictionary<string, HashSet<string>> methodFieldMap,
        Dictionary<string, HashSet<string>> methodCallMap)
    {
        // Build adjacency: two methods are connected if they share any field/property access OR
        // any private-helper-method call. The two relationships are kept in separate maps so
        // that cluster connectivity considers both, while SharedFields output only contains
        // real field/property names (BUG-N9).
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

        // Union methods that share at least one field/property OR a private-helper call.
        for (int i = 0; i < methodNames.Count; i++)
        {
            for (int j = i + 1; j < methodNames.Count; j++)
            {
                var sharesField = methodFieldMap[methodNames[i]].Overlaps(methodFieldMap[methodNames[j]]);
                var sharesCall = methodCallMap[methodNames[i]].Overlaps(methodCallMap[methodNames[j]]);
                if (sharesField || sharesCall)
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
            // SharedFields is sourced from the field map only — never from the call map.
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
