using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

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
            solution, query, SymbolFilter.All, ct).ConfigureAwait(false);

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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        return symbol is not null ? SymbolMapper.ToDto(symbol, solution) : null;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        symbol = symbol.OriginalDefinition;

        var results = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var doc = solution.GetDocument(location.SourceTree!);
            var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
            results.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var results = new List<LocationDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);

                var containingSymbol = await GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var classification = SymbolMapper.ClassifyReferenceLocation(refLocation);
                results.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview, classification));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
        var results = new List<LocationDto>();

        foreach (var impl in implementations)
        {
            foreach (var location in impl.Locations.Where(l => l.IsInSource))
            {
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
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

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return [];

        return CollectSymbols(root);
    }

    public async Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
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

        var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        var callers = new List<LocationDto>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var containingSymbol = await GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                if (containingSymbol is not null && !SymbolEqualityComparer.Default.Equals(containingSymbol, symbol))
                {
                    var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
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

                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (semanticModel is null) continue;

                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var directRefs = new List<LocationDto>();
        var affectedDeclarations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var affectedProjects = new HashSet<string>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                directRefs.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));

                if (containingSymbol is not null)
                    affectedDeclarations.Add(containingSymbol);

                affectedProjects.Add(refLocation.Document.Project.Name);
            }
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var impl in implementations)
                affectedDeclarations.Add(impl);

            var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
        return await SymbolsToLocationsAsync(overrides, solution, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        return await SymbolsToLocationsAsync(GetBaseMembers(symbol), solution, ct).ConfigureAwait(false);
    }

    public async Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var baseMembers = GetBaseMembers(symbol).Select(baseMember => SymbolMapper.ToDto(baseMember, solution)).ToList();
        var overrides = (await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false))
            .Select(overrideSymbol => SymbolMapper.ToDto(overrideSymbol, solution))
            .ToList();

        return new MemberHierarchyDto(SymbolMapper.ToDto(symbol, solution), baseMembers, overrides);
    }

    public async Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var definitions = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(location => location.IsInSource))
        {
            var document = solution.GetDocument(location.SourceTree!);
            var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct).ConfigureAwait(false) : null;
            definitions.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
        }

        var referencesTask = FindReferencesAsync(workspaceId, locator, ct);
        var implementationsTask = FindImplementationsAsync(workspaceId, locator, ct);
        var baseMembersTask = FindBaseMembersAsync(workspaceId, locator, ct);
        var overridesTask = FindOverridesAsync(workspaceId, locator, ct);
        await Task.WhenAll(referencesTask, implementationsTask, baseMembersTask, overridesTask).ConfigureAwait(false);

        return new SymbolRelationshipsDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            Definitions: definitions,
            References: await referencesTask.ConfigureAwait(false),
            Implementations: await implementationsTask.ConfigureAwait(false),
            BaseMembers: await baseMembersTask.ConfigureAwait(false),
            Overrides: await overridesTask.ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not IPropertySymbol property) return [];

        var references = await SymbolFinder.FindReferencesAsync(property, solution, ct).ConfigureAwait(false);
        var results = new List<PropertyWriteDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var refNode = root.FindNode(refLocation.Location.SourceSpan);

                // Determine if this reference is a write (left side of assignment, out/ref arg, or in an initializer)
                var isWrite = IsWriteReference(refNode);
                if (!isWrite) continue;

                var isObjectInitializer = refNode.Ancestors()
                    .Any(static a => a is InitializerExpressionSyntax init &&
                        init.Kind() == SyntaxKind.ObjectInitializerExpression);

                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var lineSpan = refLocation.Location.GetLineSpan();

                results.Add(new PropertyWriteDto(
                    FilePath: lineSpan.Path,
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    ContainingMember: containingSymbol?.ToDisplayString(),
                    PreviewText: preview,
                    IsObjectInitializer: isObjectInitializer));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var results = new List<TypeUsageDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var refNode = root.FindNode(refLocation.Location.SourceSpan);
                var classification = ClassifyTypeUsage(refNode);

                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var lineSpan = refLocation.Location.GetLineSpan();

                results.Add(new TypeUsageDto(
                    FilePath: lineSpan.Path,
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    ContainingMember: containingSymbol?.ToDisplayString(),
                    PreviewText: preview,
                    Classification: classification));
            }
        }

        return results;
    }

    private static TypeUsageClassification ClassifyTypeUsage(SyntaxNode refNode)
    {
        var parent = refNode.Parent;
        if (parent is null) return TypeUsageClassification.Other;

        // Walk up past qualified names, generic names, etc.
        var typeNode = refNode;
        while (parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or ArrayTypeSyntax)
        {
            typeNode = parent;
            parent = parent.Parent;
            if (parent is null) return TypeUsageClassification.Other;
        }

        // Handle generic type arguments (e.g., List<T>)
        if (parent is TypeArgumentListSyntax)
            return TypeUsageClassification.GenericArgument;

        // Method return type
        if (parent is MethodDeclarationSyntax methodDecl && methodDecl.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        // Local function return type
        if (parent is LocalFunctionStatementSyntax localFunc && localFunc.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        // Method parameter
        if (parent is ParameterSyntax param)
        {
            if (param.Type == typeNode)
                return TypeUsageClassification.MethodParameter;
        }

        // Property type
        if (parent is PropertyDeclarationSyntax propDecl && propDecl.Type == typeNode)
            return TypeUsageClassification.PropertyType;

        // Variable declaration — field or local
        if (parent is VariableDeclarationSyntax varDecl && varDecl.Type == typeNode)
        {
            var grandparent = varDecl.Parent;
            return grandparent is FieldDeclarationSyntax
                ? TypeUsageClassification.FieldType
                : TypeUsageClassification.LocalVariable;
        }

        // Base list (base class or interface)
        if (parent is SimpleBaseTypeSyntax || parent is BaseListSyntax)
            return TypeUsageClassification.BaseType;

        // Cast expression: (T)expr or expr as T
        if (parent is CastExpressionSyntax castExpr && castExpr.Type == typeNode)
            return TypeUsageClassification.Cast;
        if (parent is BinaryExpressionSyntax binaryAs &&
            binaryAs.IsKind(SyntaxKind.AsExpression) &&
            binaryAs.Right == typeNode)
            return TypeUsageClassification.Cast;

        // Is-pattern / type check
        if (parent is IsPatternExpressionSyntax)
            return TypeUsageClassification.TypeCheck;
        if (parent is BinaryExpressionSyntax binaryIs &&
            binaryIs.IsKind(SyntaxKind.IsExpression))
            return TypeUsageClassification.TypeCheck;
        if (parent is TypePatternSyntax || parent is DeclarationPatternSyntax)
            return TypeUsageClassification.TypeCheck;

        // Object creation: new T()
        if (parent is ObjectCreationExpressionSyntax objCreate && objCreate.Type == typeNode)
            return TypeUsageClassification.ObjectCreation;
        if (parent is ImplicitObjectCreationExpressionSyntax)
            return TypeUsageClassification.ObjectCreation;

        return TypeUsageClassification.Other;
    }

    public async Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct)
    {
        if (symbols.Count > 50)
            throw new ArgumentException("Maximum of 50 symbols per bulk request.");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var semaphore = new SemaphoreSlim(6, 6);

        async Task<BulkReferenceResultDto> ProcessOneAsync(BulkSymbolLocator bulk, int index)
        {
            var key = bulk.SymbolHandle ?? bulk.MetadataName
                ?? (bulk.FilePath is not null && bulk.Line.HasValue && bulk.Column.HasValue
                    ? $"{bulk.FilePath}:{bulk.Line}:{bulk.Column}"
                    : $"symbol[{index}]");

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var locator = ToSymbolLocator(bulk);
                var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
                if (symbol is null)
                    return new BulkReferenceResultDto(key, null, 0, [], "Symbol could not be resolved.");

                var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                var locations = new List<LocationDto>();

                if (includeDefinition)
                {
                    foreach (var loc in symbol.Locations.Where(l => l.IsInSource))
                    {
                        var doc = solution.GetDocument(loc.SourceTree!);
                        var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, loc, ct).ConfigureAwait(false) : null;
                        locations.Add(SymbolMapper.ToLocationDto(loc, symbol, preview, "Definition"));
                    }
                }

                foreach (var refSymbol in references)
                {
                    foreach (var refLocation in refSymbol.Locations)
                    {
                        var doc = refLocation.Document;
                        var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                        var containingSymbol = await GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                        var classification = SymbolMapper.ClassifyReferenceLocation(refLocation);
                        locations.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview, classification));
                    }
                }

                return new BulkReferenceResultDto(key, symbol.ToDisplayString(), locations.Count, locations, null);
            }
            catch (Exception ex)
            {
                return new BulkReferenceResultDto(key, null, 0, [], ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var tasks = symbols.Select((s, i) => ProcessOneAsync(s, i)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    public async Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol namedType) return null;

        var mutatingMembers = new List<MutatingMemberDto>();

        foreach (var member in namedType.GetMembers())
        {
            if (!IsMutatingMember(member, namedType)) continue;

            var callers = new List<MutationCallerDto>();
            var references = await SymbolFinder.FindReferencesAsync(member, solution, ct).ConfigureAwait(false);

            foreach (var refSymbol in references)
            {
                foreach (var refLocation in refSymbol.Locations)
                {
                    var containingSymbol = await GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);

                    // Skip calls from within the type itself (internal mutators calling each other)
                    if (containingSymbol is not null &&
                        SymbolEqualityComparer.Default.Equals(containingSymbol.ContainingType, namedType))
                        continue;

                    var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                    var phase = await ClassifyCallerPhaseAsync(refLocation, namedType, ct).ConfigureAwait(false);
                    var lineSpan = refLocation.Location.GetLineSpan();

                    callers.Add(new MutationCallerDto(
                        FilePath: lineSpan.Path,
                        StartLine: lineSpan.StartLinePosition.Line + 1,
                        StartColumn: lineSpan.StartLinePosition.Character + 1,
                        ContainingMember: containingSymbol?.ToDisplayString(),
                        PreviewText: preview,
                        CallerPhase: phase));
                }
            }

            var memberLoc = member.Locations.FirstOrDefault(l => l.IsInSource);
            mutatingMembers.Add(new MutatingMemberDto(
                Name: member.Name,
                FullyQualifiedName: member.ToDisplayString(),
                Kind: member.Kind.ToString(),
                FilePath: memberLoc?.GetLineSpan().Path,
                Line: memberLoc?.GetLineSpan().StartLinePosition.Line + 1,
                ExternalCallers: callers));
        }

        var summary = $"Type '{namedType.Name}' has {mutatingMembers.Count} mutating member(s) " +
                      $"with {mutatingMembers.Sum(m => m.ExternalCallers.Count)} external caller(s).";

        return new TypeMutationDto(
            Type: SymbolMapper.ToDto(namedType, solution),
            MutatingMembers: mutatingMembers,
            Summary: summary);
    }

    private static bool IsMutatingMember(ISymbol member, INamedTypeSymbol containingType)
    {
        if (member.IsStatic) return false;
        if (member.DeclaredAccessibility == Accessibility.Private) return false;

        // Settable properties
        if (member is IPropertySymbol prop && prop.SetMethod is not null &&
            !prop.SetMethod.IsInitOnly && prop.SetMethod.DeclaredAccessibility != Accessibility.Private)
            return true;

        // Methods that write to instance fields/properties (simple heuristic: check method body)
        if (member is IMethodSymbol method &&
            method.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
        {
            var location = method.Locations.FirstOrDefault(l => l.IsInSource);
            if (location?.SourceTree is null) return false;

            var root = location.SourceTree.GetRoot();
            var methodNode = root.FindNode(location.SourceSpan);

            var hasInstanceAssignment = methodNode.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(a =>
                {
                    var left = a.Left;
                    if (left is MemberAccessExpressionSyntax memberAccess)
                    {
                        return memberAccess.Expression is ThisExpressionSyntax ||
                               (memberAccess.Expression is IdentifierNameSyntax && IsInstanceMember(memberAccess.Name.Identifier.Text, containingType));
                    }
                    if (left is IdentifierNameSyntax ident)
                    {
                        return IsInstanceMember(ident.Identifier.Text, containingType);
                    }
                    return false;
                });

            if (hasInstanceAssignment) return true;

            // Methods that call mutating collection methods (.Add, .Remove, .Clear) on instance members
            var hasMutatingCollectionCall = methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv =>
                {
                    if (inv.Expression is MemberAccessExpressionSyntax access)
                    {
                        var methodName = access.Name.Identifier.Text;
                        return methodName is "Add" or "Remove" or "Clear" or "Insert" or "RemoveAt" or "AddRange" or "RemoveAll";
                    }
                    return false;
                });

            return hasMutatingCollectionCall;
        }

        return false;
    }

    private static bool IsInstanceMember(string name, INamedTypeSymbol type)
    {
        return type.GetMembers(name).Any(m => !m.IsStatic);
    }

    private static async Task<string> ClassifyCallerPhaseAsync(ReferenceLocation refLocation, INamedTypeSymbol targetType, CancellationToken ct)
    {
        var root = await refLocation.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return "Unknown";

        var node = root.FindNode(refLocation.Location.SourceSpan);

        // Check if inside an object initializer
        if (node.Ancestors().OfType<InitializerExpressionSyntax>()
            .Any(e => e.Kind() == SyntaxKind.ObjectInitializerExpression))
            return "Construction";

        // Check if inside a constructor
        if (node.Ancestors().OfType<ConstructorDeclarationSyntax>().Any())
            return "Construction";

        // Check if inside a method of the same type that returns void (builder-pattern heuristic)
        var enclosingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is not null)
        {
            var returnType = enclosingMethod.ReturnType.ToString();
            if (returnType is "void" or "Void")
            {
                var semanticModel = await refLocation.Document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (semanticModel is not null)
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(enclosingMethod, ct);
                    if (methodSymbol?.ContainingType is not null &&
                        SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, targetType))
                        return "Construction";
                }
            }
        }

        return "PostConstruction";
    }

    public async Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return null;

        var text = await syntaxTree.GetTextAsync(ct).ConfigureAwait(false);
        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
        var node = root.FindToken(position).Parent;

        while (node is not null)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(node, ct);
                if (declaredSymbol is not null)
                    return SymbolMapper.ToDto(declaredSymbol, solution);
            }
            node = node.Parent;
        }

        return null;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        INamedTypeSymbol? typeSymbol = symbol switch
        {
            ILocalSymbol local => local.Type as INamedTypeSymbol,
            IParameterSymbol param => param.Type as INamedTypeSymbol,
            IFieldSymbol field => field.Type as INamedTypeSymbol,
            IPropertySymbol prop => prop.Type as INamedTypeSymbol,
            IEventSymbol evt => evt.Type as INamedTypeSymbol,
            IMethodSymbol method => method.ReturnType as INamedTypeSymbol,
            INamedTypeSymbol namedType => namedType,
            _ => null
        };

        if (typeSymbol is null) return [];

        var results = new List<LocationDto>();
        foreach (var location in typeSymbol.Locations.Where(l => l.IsInSource))
        {
            var doc = solution.GetDocument(location.SourceTree!);
            var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
            results.Add(SymbolMapper.ToLocationDto(location, typeSymbol, preview));
        }

        return results;
    }

    private static SymbolLocator ToSymbolLocator(BulkSymbolLocator bulk)
    {
        if (!string.IsNullOrWhiteSpace(bulk.SymbolHandle))
            return SymbolLocator.ByHandle(bulk.SymbolHandle);
        if (!string.IsNullOrWhiteSpace(bulk.MetadataName))
            return SymbolLocator.ByMetadataName(bulk.MetadataName);
        if (!string.IsNullOrWhiteSpace(bulk.FilePath) && bulk.Line.HasValue && bulk.Column.HasValue)
            return SymbolLocator.BySource(bulk.FilePath, bulk.Line.Value, bulk.Column.Value);
        throw new ArgumentException("BulkSymbolLocator requires symbolHandle, metadataName, or filePath/line/column.");
    }

    private static bool IsWriteReference(SyntaxNode refNode)
    {
        // Object initializer member assignment: { Prop = value }
        if (refNode.Parent is AssignmentExpressionSyntax assignInInit &&
            assignInInit.Left == refNode &&
            assignInInit.Parent is InitializerExpressionSyntax)
            return true;

        // Regular assignment: target = value
        if (refNode.Parent is AssignmentExpressionSyntax directAssign && directAssign.Left == refNode)
            return true;

        // MemberAccess on left of assignment: obj.Prop = value
        if (refNode.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is AssignmentExpressionSyntax memberAssign &&
            memberAssign.Left == memberAccess)
            return true;

        // out or ref argument
        if (refNode.Parent is ArgumentSyntax arg &&
            (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)))
            return true;

        return false;
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
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
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
                var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct).ConfigureAwait(false) : null;
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
