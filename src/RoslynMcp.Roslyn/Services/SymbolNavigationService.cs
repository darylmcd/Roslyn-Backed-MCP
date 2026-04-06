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

        // Provide descriptive message for built-in/primitive types that have no source locations
        if (!typeSymbol.Locations.Any(l => l.IsInSource) && typeSymbol.SpecialType != SpecialType.None)
        {
            throw new InvalidOperationException(
                $"Cannot navigate to type definition for built-in type '{typeSymbol.ToDisplayString()}' — " +
                "it is defined in the .NET runtime, not in source code.");
        }

        var results = new List<LocationDto>();
        foreach (var location in typeSymbol.Locations.Where(l => l.IsInSource))
        {
            var doc = solution.GetDocument(location.SourceTree!);
            var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
            results.Add(SymbolMapper.ToLocationDto(location, typeSymbol, preview, "Definition"));
        }

        return results;
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
