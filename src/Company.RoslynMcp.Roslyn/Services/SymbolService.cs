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
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SymbolDto>();
        HashSet<string>? allowedProjectPaths = null;

        if (projectFilter is not null)
        {
            allowedProjectPaths = solution.Projects
                .Where(project => string.Equals(project.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
                .SelectMany(project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                .Select(document => Path.GetFullPath(document.FilePath!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedProjectPaths.Count == 0)
            {
                return [];
            }
        }

        var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
            solution, query, SymbolFilter.All, ct);

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) break;
            if (results.Count >= limit) break;

            if (kindFilter is not null && !MatchesKind(symbol, kindFilter))
                continue;

            if (namespaceFilter is not null && symbol.ContainingNamespace?.ToDisplayString() != namespaceFilter)
                continue;

            if (allowedProjectPaths is not null)
            {
                var inAllowedProject = symbol.Locations.Any(location =>
                    location.IsInSource &&
                    allowedProjectPaths.Contains(Path.GetFullPath(location.GetLineSpan().Path)));
                if (!inAllowedProject)
                {
                    continue;
                }
            }

            var dto = SymbolMapper.ToDto(symbol, solution);
            results.Add(dto);
        }

        return results;
    }

    public async Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        return symbol is not null ? SymbolMapper.ToDto(symbol, solution) : null;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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

    public async Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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

    public async Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return [];

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return [];

        return CollectSymbols(root);
    }

    public async Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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

    public async Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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
            SymbolMapper.ToDto(symbol, solution),
            callers,
            callees);
    }

    public async Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
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
            SymbolMapper.ToDto(symbol, solution),
            directRefs,
            affectedDeclarations.Select(s => SymbolMapper.ToDto(s, solution)).ToList(),
            affectedProjects.ToList(),
            summary);
    }

    public async Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return [];
        }

        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct);
        return await SymbolsToLocationsAsync(overrides, solution, ct);
    }

    public async Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return [];
        }

        return await SymbolsToLocationsAsync(GetBaseMembers(symbol), solution, ct);
    }

    public async Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return null;
        }

        var baseMembers = GetBaseMembers(symbol).Select(baseMember => SymbolMapper.ToDto(baseMember, solution)).ToList();
        var overrides = (await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct))
            .Select(overrideSymbol => SymbolMapper.ToDto(overrideSymbol, solution))
            .ToList();

        return new MemberHierarchyDto(SymbolMapper.ToDto(symbol, solution), baseMembers, overrides);
    }

    public async Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return null;
        }

        var dto = SymbolMapper.ToDto(symbol, solution);
        var parameters = symbol is IMethodSymbol method
            ? method.Parameters
                .Select(parameter => $"{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}")
                .ToList()
            : dto.Parameters ?? [];

        return new SignatureHelpDto(
            DisplaySignature: symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ReturnType: dto.ReturnType,
            Parameters: parameters,
            Documentation: dto.Documentation);
    }

    public async Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return null;
        }

        var definitions = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(location => location.IsInSource))
        {
            var document = solution.GetDocument(location.SourceTree!);
            var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct) : null;
            definitions.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        var referencesTask = FindReferencesAsync(workspaceId, locator, ct);
        var implementationsTask = FindImplementationsAsync(workspaceId, locator, ct);
        var baseMembersTask = FindBaseMembersAsync(workspaceId, locator, ct);
        var overridesTask = FindOverridesAsync(workspaceId, locator, ct);
        await Task.WhenAll(referencesTask, implementationsTask, baseMembersTask, overridesTask);

        return new SymbolRelationshipsDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            Definitions: definitions,
            References: await referencesTask,
            Implementations: await implementationsTask,
            BaseMembers: await baseMembersTask,
            Overrides: await overridesTask);
    }

    private static bool MatchesKind(ISymbol symbol, string kindFilter)
    {
        var kinds = new[]
        {
            symbol.Kind.ToString(),
            symbol is INamedTypeSymbol namedType ? namedType.TypeKind.ToString() : null
        };

        return kinds.Any(kind => string.Equals(kind, kindFilter, StringComparison.OrdinalIgnoreCase));
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

    private static IEnumerable<ISymbol> GetBaseMembers(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => EnumerateMethodBases(method),
            IPropertySymbol property => EnumeratePropertyBases(property),
            IEventSymbol eventSymbol => EnumerateEventBases(eventSymbol),
            INamedTypeSymbol namedType => EnumerateTypeBases(namedType),
            _ => []
        };
    }

    private static IEnumerable<ISymbol> EnumerateMethodBases(IMethodSymbol method)
    {
        var current = method.OverriddenMethod;
        while (current is not null)
        {
            yield return current;
            current = current.OverriddenMethod;
        }

        foreach (var explicitImplementation in method.ExplicitInterfaceImplementations)
        {
            yield return explicitImplementation;
        }
    }

    private static IEnumerable<ISymbol> EnumeratePropertyBases(IPropertySymbol property)
    {
        var current = property.OverriddenProperty;
        while (current is not null)
        {
            yield return current;
            current = current.OverriddenProperty;
        }

        foreach (var explicitImplementation in property.ExplicitInterfaceImplementations)
        {
            yield return explicitImplementation;
        }
    }

    private static IEnumerable<ISymbol> EnumerateEventBases(IEventSymbol eventSymbol)
    {
        var current = eventSymbol.OverriddenEvent;
        while (current is not null)
        {
            yield return current;
            current = current.OverriddenEvent;
        }

        foreach (var explicitImplementation in eventSymbol.ExplicitInterfaceImplementations)
        {
            yield return explicitImplementation;
        }
    }

    private static IEnumerable<ISymbol> EnumerateTypeBases(INamedTypeSymbol namedType)
    {
        var current = namedType.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            yield return current;
            current = current.BaseType;
        }

        foreach (var interfaceSymbol in namedType.Interfaces)
        {
            yield return interfaceSymbol;
        }
    }

    private static async Task<IReadOnlyList<LocationDto>> SymbolsToLocationsAsync(
        IEnumerable<ISymbol> symbols,
        Solution solution,
        CancellationToken ct)
    {
        var results = new List<LocationDto>();
        foreach (var symbol in symbols.Distinct(SymbolEqualityComparer.Default))
        {
            foreach (var location in symbol.Locations.Where(location => location.IsInSource))
            {
                var document = solution.GetDocument(location.SourceTree!);
                var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct) : null;
                results.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
            }
        }

        return results;
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
