using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class SymbolService : ISymbolService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SymbolService> _logger;

    public SymbolService(IWorkspaceManager workspace, ILogger<SymbolService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var results = new List<SymbolDto>();

        var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
            solution, query, SymbolFilter.All, ct);

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) break;
            if (results.Count >= limit) break;

            if (kindFilter is not null && !string.Equals(symbol.Kind.ToString(), kindFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (namespaceFilter is not null && symbol.ContainingNamespace?.ToDisplayString() != namespaceFilter)
                continue;

            if (projectFilter is not null)
            {
                var symbolProject = solution.Projects.FirstOrDefault(p =>
                    p.Documents.Any(d => symbol.Locations.Any(l =>
                        l.IsInSource && d.FilePath is not null &&
                        string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(l.GetLineSpan().Path), StringComparison.OrdinalIgnoreCase))));
                if (symbolProject is null || !string.Equals(symbolProject.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var dto = SymbolMapper.ToDto(symbol);
            results.Add(dto);
        }

        return results;
    }

    public async Task<SymbolDto?> GetSymbolInfoAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        return symbol is not null ? SymbolMapper.ToDto(symbol) : null;
    }

    public async Task<SymbolDto?> GetSymbolInfoByNameAsync(string fullyQualifiedName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var type = compilation.GetTypeByMetadataName(fullyQualifiedName);
            if (type is not null) return SymbolMapper.ToDto(type);
        }
        return null;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null) return [];

        symbol = symbol.OriginalDefinition;

        var results = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var doc = solution.GetDocument(location.SourceTree!);
            var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct) : null;
            results.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
        var results = new List<LocationDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct);

                var containingSymbol = await GetContainingSymbolAsync(doc, refLocation.Location, ct);
                results.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null) return [];

        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct);
        var results = new List<LocationDto>();

        foreach (var impl in implementations)
        {
            foreach (var location in impl.Locations.Where(l => l.IsInSource))
            {
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct) : null;
                results.Add(SymbolMapper.ToLocationDto(location, impl, preview));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return [];

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return [];

        return CollectSymbols(root);
    }

    public async Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is not INamedTypeSymbol namedType) return null;

        var baseTypes = new List<TypeHierarchyDto>();
        var current = namedType.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            var loc = current.Locations.FirstOrDefault(l => l.IsInSource);
            baseTypes.Add(new TypeHierarchyDto(
                current.Name, current.ToDisplayString(),
                loc?.GetLineSpan().Path, loc?.GetLineSpan().StartLinePosition.Line + 1,
                null, null, null));
            current = current.BaseType;
        }

        var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct);
        var derivedTypes = derivedClasses.Select(d =>
        {
            var loc = d.Locations.FirstOrDefault(l => l.IsInSource);
            return new TypeHierarchyDto(
                d.Name, d.ToDisplayString(),
                loc?.GetLineSpan().Path, loc?.GetLineSpan().StartLinePosition.Line + 1,
                null, null, null);
        }).ToList();

        var interfacesList = namedType.Interfaces.Select(i =>
        {
            var loc = i.Locations.FirstOrDefault(l => l.IsInSource);
            return new TypeHierarchyDto(
                i.Name, i.ToDisplayString(),
                loc?.GetLineSpan().Path, loc?.GetLineSpan().StartLinePosition.Line + 1,
                null, null, null);
        }).ToList();

        var selfLoc = namedType.Locations.FirstOrDefault(l => l.IsInSource);
        return new TypeHierarchyDto(
            namedType.Name, namedType.ToDisplayString(),
            selfLoc?.GetLineSpan().Path, selfLoc?.GetLineSpan().StartLinePosition.Line + 1,
            baseTypes.Count > 0 ? baseTypes : null,
            derivedTypes.Count > 0 ? derivedTypes : null,
            interfacesList.Count > 0 ? interfacesList : null);
    }

    public async Task<CallerCalleeDto?> GetCallersCalleesAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null) return null;

        var callers = new List<LocationDto>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var containingSymbol = await GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct);
                if (containingSymbol is not null && !SymbolEqualityComparer.Default.Equals(containingSymbol, symbol))
                {
                    var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct);
                    callers.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));
                }
            }
        }

        var callees = new List<LocationDto>();
        if (symbol is IMethodSymbol methodSymbol)
        {
            foreach (var location in methodSymbol.Locations.Where(l => l.IsInSource))
            {
                var tree = location.SourceTree;
                if (tree is null) continue;

                var doc = solution.GetDocument(tree);
                if (doc is null) continue;

                var semanticModel = await doc.GetSemanticModelAsync(ct);
                if (semanticModel is null) continue;

                var root = await tree.GetRootAsync(ct);
                var methodNode = root.FindNode(location.SourceSpan);

                var invocations = methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var invokedSymbol = semanticModel.GetSymbolInfo(invocation, ct).Symbol;
                    if (invokedSymbol is not null)
                    {
                        var invokedLoc = invokedSymbol.Locations.FirstOrDefault(l => l.IsInSource) ?? invocation.GetLocation();
                        callees.Add(SymbolMapper.ToLocationDto(invokedLoc, invokedSymbol));
                    }
                }
            }
        }

        return new CallerCalleeDto(
            SymbolMapper.ToDto(symbol),
            callers,
            callees);
    }

    public async Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null) return null;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
        var directRefs = new List<LocationDto>();
        var affectedDeclarations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var affectedProjects = new HashSet<string>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct);
                var containingSymbol = await GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct);
                directRefs.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));

                if (containingSymbol is not null)
                    affectedDeclarations.Add(containingSymbol);

                affectedProjects.Add(refLocation.Document.Project.Name);
            }
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct);
            foreach (var impl in implementations)
                affectedDeclarations.Add(impl);

            var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct);
            foreach (var d in derived)
                affectedDeclarations.Add(d);
        }

        var summary = $"Symbol '{symbol.Name}' has {directRefs.Count} reference(s) across {affectedProjects.Count} project(s), " +
                       $"affecting {affectedDeclarations.Count} declaration(s).";

        return new ImpactAnalysisDto(
            SymbolMapper.ToDto(symbol),
            directRefs,
            affectedDeclarations.Select(s => SymbolMapper.ToDto(s)).ToList(),
            affectedProjects.ToList(),
            summary);
    }

    private static async Task<ISymbol?> GetContainingSymbolAsync(Document document, Location location, CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return null;

        var node = root.FindNode(location.SourceSpan);
        while (node is not null)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                return semanticModel.GetDeclaredSymbol(node, ct);
            }
            node = node.Parent;
        }
        return null;
    }

    private static IReadOnlyList<DocumentSymbolDto> CollectSymbols(SyntaxNode node)
    {
        var symbols = new List<DocumentSymbolDto>();

        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case NamespaceDeclarationSyntax ns:
                    symbols.Add(new DocumentSymbolDto(
                        ns.Name.ToString(), "Namespace", null,
                        ns.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        ns.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectSymbols(ns)));
                    break;
                case FileScopedNamespaceDeclarationSyntax fns:
                    symbols.AddRange(CollectSymbols(fns));
                    break;
                case TypeDeclarationSyntax typeDecl:
                    var kind = typeDecl switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "RecordStruct" : "Record",
                        _ => "Type"
                    };
                    symbols.Add(new DocumentSymbolDto(
                        typeDecl.Identifier.Text, kind,
                        typeDecl.Modifiers.Select(m => m.Text).ToList(),
                        typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectMembers(typeDecl)));
                    break;
                case EnumDeclarationSyntax enumDecl:
                    symbols.Add(new DocumentSymbolDto(
                        enumDecl.Identifier.Text, "Enum",
                        enumDecl.Modifiers.Select(m => m.Text).ToList(),
                        enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        enumDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        enumDecl.Members.Select(m => new DocumentSymbolDto(
                            m.Identifier.Text, "EnumMember", null,
                            m.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            m.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            null)).ToList()));
                    break;
                case DelegateDeclarationSyntax delegateDecl:
                    symbols.Add(new DocumentSymbolDto(
                        delegateDecl.Identifier.Text, "Delegate",
                        delegateDecl.Modifiers.Select(m => m.Text).ToList(),
                        delegateDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        delegateDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case GlobalStatementSyntax:
                    break;
            }
        }

        return symbols;
    }

    private static IReadOnlyList<DocumentSymbolDto> CollectMembers(TypeDeclarationSyntax typeDecl)
    {
        var members = new List<DocumentSymbolDto>();

        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    members.Add(new DocumentSymbolDto(
                        method.Identifier.Text, "Method",
                        method.Modifiers.Select(m => m.Text).ToList(),
                        method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        method.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case PropertyDeclarationSyntax prop:
                    members.Add(new DocumentSymbolDto(
                        prop.Identifier.Text, "Property",
                        prop.Modifiers.Select(m => m.Text).ToList(),
                        prop.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        prop.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        members.Add(new DocumentSymbolDto(
                            variable.Identifier.Text, "Field",
                            field.Modifiers.Select(m => m.Text).ToList(),
                            field.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            field.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            null));
                    }
                    break;
                case EventDeclarationSyntax evt:
                    members.Add(new DocumentSymbolDto(
                        evt.Identifier.Text, "Event",
                        evt.Modifiers.Select(m => m.Text).ToList(),
                        evt.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        evt.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case ConstructorDeclarationSyntax ctor:
                    members.Add(new DocumentSymbolDto(
                        ctor.Identifier.Text, "Constructor",
                        ctor.Modifiers.Select(m => m.Text).ToList(),
                        ctor.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        ctor.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case TypeDeclarationSyntax nestedType:
                    var kind = nestedType switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax => "Record",
                        _ => "Type"
                    };
                    members.Add(new DocumentSymbolDto(
                        nestedType.Identifier.Text, kind,
                        nestedType.Modifiers.Select(m => m.Text).ToList(),
                        nestedType.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        nestedType.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectMembers(nestedType)));
                    break;
            }
        }

        return members;
    }
}
