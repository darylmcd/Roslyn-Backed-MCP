using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class SymbolNavigationService : ISymbolNavigationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SymbolNavigationService> _logger;

    public SymbolNavigationService(IWorkspaceManager workspace, ILogger<SymbolNavigationService> logger)
    {
        _workspace = workspace;
        _logger = logger;
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
            results.Add(SymbolMapper.ToLocationDto(location, symbol, preview, "Definition"));
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        // goto-type-definition-local-vars: extract ITypeSymbol (not just INamedTypeSymbol) so
        // arrays, type parameters, and pointers carry through. Empty result for primitives is
        // still handled by the SpecialType guard below.
        ITypeSymbol? typeSymbol = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol prop => prop.Type,
            IEventSymbol evt => evt.Type,
            IMethodSymbol method => method.ReturnType,
            ITypeSymbol type => type,
            _ => null
        };

        if (typeSymbol is null) return [];

        var results = new List<LocationDto>();
        var seenLocations = new HashSet<Location>();

        // goto-type-definition-local-vars: walk constructed-generic type arguments when the
        // outer type is in metadata (e.g. IEnumerable<UserDto> → UserDto). Also unwrap arrays
        // and pointers to their element type so `var users = new UserDto[]{}` navigates to
        // UserDto.
        foreach (var candidate in EnumerateTypeCandidates(typeSymbol))
        {
            foreach (var location in candidate.Locations.Where(l => l.IsInSource))
            {
                if (!seenLocations.Add(location)) continue;
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
                results.Add(SymbolMapper.ToLocationDto(location, candidate, preview, "Definition"));
            }
        }

        if (results.Count > 0)
        {
            return results;
        }

        // Provide descriptive message when nothing in the type's hierarchy has a source location
        // (built-in/primitive types, BCL generics whose arguments are also in metadata, etc.).
        throw new InvalidOperationException(
            $"Cannot navigate to type definition for '{typeSymbol.ToDisplayString()}' — " +
            "neither the type nor any of its type arguments are defined in source. " +
            "This typically means the type is defined in the .NET runtime or an external assembly.");
    }

    /// <summary>
    /// Yields the type itself, then peels off layers Roslyn does NOT collapse via
    /// <see cref="ISymbol.Locations"/> — array element type, pointer pointed-at type, and
    /// constructed generic type arguments — so callers can navigate from a variable typed
    /// <c>UserDto[]</c> or <c>IEnumerable&lt;UserDto&gt;</c> to <c>UserDto</c>'s source.
    /// </summary>
    private static IEnumerable<ITypeSymbol> EnumerateTypeCandidates(ITypeSymbol root)
    {
        var queue = new Queue<ITypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;

            switch (current)
            {
                case IArrayTypeSymbol array:
                    queue.Enqueue(array.ElementType);
                    break;
                case IPointerTypeSymbol pointer:
                    queue.Enqueue(pointer.PointedAtType);
                    break;
                case INamedTypeSymbol named when named.IsGenericType:
                    foreach (var arg in named.TypeArguments)
                        queue.Enqueue(arg);
                    break;
            }
        }
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
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

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
}
