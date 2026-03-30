using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class SymbolRelationshipService : ISymbolRelationshipService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IReferenceService _referenceService;
    private readonly ILogger<SymbolRelationshipService> _logger;

    public SymbolRelationshipService(IWorkspaceManager workspace, IReferenceService referenceService, ILogger<SymbolRelationshipService> logger)
    {
        _workspace = workspace;
        _referenceService = referenceService;
        _logger = logger;
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

    public async Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var baseMembers = SymbolServiceHelpers.GetBaseMembers(symbol).Select(baseMember => SymbolMapper.ToDto(baseMember, solution)).ToList();
        var overrides = (await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false))
            .Select(overrideSymbol => SymbolMapper.ToDto(overrideSymbol, solution))
            .ToList();

        return new MemberHierarchyDto(SymbolMapper.ToDto(symbol, solution), baseMembers, overrides);
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

        var referencesTask = _referenceService.FindReferencesAsync(workspaceId, locator, ct);
        var implementationsTask = _referenceService.FindImplementationsAsync(workspaceId, locator, ct);
        var baseMembersTask = _referenceService.FindBaseMembersAsync(workspaceId, locator, ct);
        var overridesTask = _referenceService.FindOverridesAsync(workspaceId, locator, ct);
        await Task.WhenAll(referencesTask, implementationsTask, baseMembersTask, overridesTask).ConfigureAwait(false);

        return new SymbolRelationshipsDto(
            Symbol: SymbolMapper.ToDto(symbol, solution),
            Definitions: definitions,
            References: await referencesTask.ConfigureAwait(false) ?? [],
            Implementations: await implementationsTask.ConfigureAwait(false) ?? [],
            BaseMembers: await baseMembersTask.ConfigureAwait(false) ?? [],
            Overrides: await overridesTask.ConfigureAwait(false) ?? []);
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

    public async Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        // If the resolved symbol is a type (e.g. Task<T> from an async method's return type),
        // try to find the enclosing method declaration instead
        if (symbol is INamedTypeSymbol && locator.HasSourceLocation)
        {
            var enclosingMethod = await TryResolveEnclosingMethodAsync(solution, locator, ct).ConfigureAwait(false);
            if (enclosingMethod is not null)
                symbol = enclosingMethod;
        }

        var callers = new List<LocationDto>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
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

    private static async Task<IMethodSymbol?> TryResolveEnclosingMethodAsync(
        Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        if (locator.FilePath is null || locator.Line is null || locator.Column is null) return null;

        var document = SymbolResolver.FindDocument(solution, locator.FilePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (semanticModel is null || tree is null) return null;

        var text = tree.GetText(ct);
        var position = text.Lines[locator.Line.Value - 1].Start + (locator.Column.Value - 1);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var node = root.FindToken(position).Parent;
        while (node is not null)
        {
            if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                var declared = semanticModel.GetDeclaredSymbol(node, ct);
                if (declared is IMethodSymbol method)
                    return method;
            }
            node = node.Parent;
        }

        return null;
    }
}
