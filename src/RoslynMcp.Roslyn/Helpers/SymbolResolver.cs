using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Resolves Roslyn <see cref="ISymbol"/> instances from a <see cref="SymbolLocator"/> within a
/// loaded <see cref="Solution"/>.
/// </summary>
public static class SymbolResolver
{
    /// <summary>
    /// Resolves a symbol from the given <paramref name="locator"/> using whichever identification
    /// strategy is set (handle, metadata name, or source position).
    /// </summary>
    /// <param name="solution">The solution to search.</param>
    /// <param name="locator">The symbol locator describing how to find the symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved symbol, or <see langword="null"/> if it could not be found.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="locator"/> does not contain a valid identification strategy.</exception>
    public static async Task<ISymbol?> ResolveAsync(Solution solution, SymbolLocator locator, CancellationToken ct)
    {
        locator.Validate();

        if (locator.HasHandle)
        {
            return await SymbolHandleSerializer.ResolveHandleAsync(solution, locator.SymbolHandle!, ct).ConfigureAwait(false);
        }

        if (locator.HasMetadataName)
        {
            return await ResolveByMetadataNameAsync(solution, locator.MetadataName!, ct).ConfigureAwait(false);
        }

        return await ResolveAtPositionAsync(solution, locator.FilePath!, locator.Line!.Value, locator.Column!.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the symbol at the given 1-based <paramref name="line"/> and <paramref name="column"/> position
    /// in the specified source file.
    /// </summary>
    /// <returns>The resolved symbol, or <see langword="null"/> if no symbol exists at that position.</returns>
    public static async Task<ISymbol?> ResolveAtPositionAsync(Solution solution, string filePath, int line, int column, CancellationToken ct)
    {
        var document = FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return null;

        var text = syntaxTree.GetText(ct);
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);

        // Try the token at the exact position first, then also try FindToken with
        // findInsideTrivia to handle mid-identifier cursor positions (AUDIT-05).
        var token = root.FindToken(position);
        var result = ResolveFromToken(token, semanticModel, ct);
        if (result is not null) return result;

        // If the position falls within the token's full span but didn't resolve,
        // try the preceding token (handles cases like cursor on '(' after a method name).
        if (position > 0)
        {
            var precedingToken = root.FindToken(position - 1);
            if (precedingToken != token)
            {
                result = ResolveFromToken(precedingToken, semanticModel, ct);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private static ISymbol? ResolveFromToken(SyntaxToken token, SemanticModel semanticModel, CancellationToken ct)
    {
        var node = token.Parent;
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

    /// <summary>
    /// Searches all projects in <paramref name="solution"/> for a named type with the given metadata name.
    /// </summary>
    /// <returns>The first matching symbol, or <see langword="null"/> if not found.</returns>
    public static async Task<ISymbol?> ResolveByMetadataNameAsync(Solution solution, string metadataName, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
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

    /// <summary>
    /// Finds the first document in <paramref name="solution"/> whose file path matches <paramref name="filePath"/>
    /// (case-insensitive on all platforms).
    /// </summary>
    /// <returns>The matching document, or <see langword="null"/> if not found.</returns>
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

    /// <summary>
    /// FLAG-006: When the caret lands on a member's type token (e.g. the return-type token of a
    /// method declaration), <see cref="ResolveAtPositionAsync"/> resolves the *type* symbol rather
    /// than the enclosing member. This helper walks the syntax tree upward from the caret token
    /// looking for an enclosing member declaration (method, local function, or property) and
    /// returns its declared symbol so callers can promote a type-token resolution to the more
    /// useful declaring-member resolution.
    /// </summary>
    /// <returns>
    /// The enclosing <see cref="IMethodSymbol"/> or <see cref="IPropertySymbol"/>, or
    /// <see langword="null"/> if no enclosing member is found.
    /// </returns>
    public static async Task<ISymbol?> TryResolveEnclosingMemberAsync(
        Solution solution, string filePath, int line, int column, CancellationToken ct)
    {
        var document = FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (semanticModel is null || tree is null) return null;

        var text = tree.GetText(ct);
        if (line < 1 || line > text.Lines.Count) return null;

        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var node = root.FindToken(position).Parent;
        while (node is not null)
        {
            if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or PropertyDeclarationSyntax)
            {
                var declared = semanticModel.GetDeclaredSymbol(node, ct);
                if (declared is IMethodSymbol or IPropertySymbol)
                    return declared;
            }
            node = node.Parent;
        }

        return null;
    }

    public static async Task<string?> GetPreviewTextAsync(Document document, Location location, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid) return null;

        var lineNumber = lineSpan.StartLinePosition.Line;
        if (lineNumber >= text.Lines.Count) return null;

        return text.Lines[lineNumber].ToString().Trim();
    }
}
