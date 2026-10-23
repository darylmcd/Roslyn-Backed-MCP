using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

internal static class ExtractedInterfaceReference
{
    internal static async Task<TypeSyntax> ReadAsync(Document document, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var declaration = root?.DescendantNodes().OfType<InterfaceDeclarationSyntax>().SingleOrDefault()
            ?? throw new PublicInvalidOperationException("The extracted interface declaration is unavailable.");
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var symbol = model?.GetDeclaredSymbol(declaration, ct)
            ?? throw new PublicInvalidOperationException("The extracted interface symbol is unavailable.");
        return SyntaxFactory.ParseTypeName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}
