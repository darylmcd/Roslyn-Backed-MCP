using Company.RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Company.RoslynMcp.Roslyn.Helpers;

public static class SymbolResolver
{
    public static async Task<ISymbol?> ResolveAsync(Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        locator.Validate();

        if (locator.HasHandle)
        {
            return await SymbolHandleSerializer.ResolveHandleAsync(solution, locator.SymbolHandle!, ct);
        }

        if (locator.HasMetadataName)
        {
            return await ResolveByMetadataNameAsync(solution, locator.MetadataName!, ct);
        }

        return await ResolveAtPositionAsync(solution, locator.FilePath!, locator.Line!.Value, locator.Column!.Value, ct);
    }

    public static async Task<ISymbol?> ResolveAtPositionAsync(Solution solution, string filePath, int line, int column, CancellationToken ct)
    {
        var document = FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct);
        if (syntaxTree is null) return null;

        var position = syntaxTree.GetText(ct).Lines[line - 1].Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct);
        var node = root.FindToken(position).Parent;

        while (node is not null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(node, ct);
            if (symbolInfo.Symbol is not null)
                return symbolInfo.Symbol;

            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, ct);
            if (declaredSymbol is not null)
                return declaredSymbol;

            node = node.Parent;
        }

        return null;
    }

    public static async Task<ISymbol?> ResolveByMetadataNameAsync(Solution solution, string metadataName, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
            {
                continue;
            }

            var symbol = compilation.GetTypeByMetadataName(metadataName);
            if (symbol is not null)
            {
                return symbol;
            }
        }

        return null;
    }

    public static Document? FindDocument(Solution solution, string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is not null &&
                    string.Equals(Path.GetFullPath(document.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return document;
                }
            }
        }
        return null;
    }

    public static async Task<string?> GetPreviewTextAsync(Document document, Location location, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct);
        var lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid) return null;

        var lineNumber = lineSpan.StartLinePosition.Line;
        if (lineNumber >= text.Lines.Count) return null;

        return text.Lines[lineNumber].ToString().Trim();
    }
}
