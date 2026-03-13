using Company.RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Roslyn.Helpers;

public static class SymbolMapper
{
    public static SymbolDto ToDto(ISymbol symbol, Location? primaryLocation = null)
    {
        var location = primaryLocation ?? symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var lineSpan = location?.GetLineSpan();

        string? returnType = symbol switch
        {
            IMethodSymbol m => m.ReturnType.ToDisplayString(),
            IPropertySymbol p => p.Type.ToDisplayString(),
            IFieldSymbol f => f.Type.ToDisplayString(),
            IEventSymbol e => e.Type.ToDisplayString(),
            _ => null
        };

        var parameters = symbol is IMethodSymbol method
            ? method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}").ToList()
            : null;

        var modifiers = GetModifiers(symbol);

        IReadOnlyList<string>? baseTypes = null;
        IReadOnlyList<string>? interfaces = null;
        if (symbol is INamedTypeSymbol namedType)
        {
            baseTypes = namedType.BaseType is not null && namedType.BaseType.SpecialType != SpecialType.System_Object
                ? [namedType.BaseType.ToDisplayString()]
                : null;
            interfaces = namedType.Interfaces.Length > 0
                ? namedType.Interfaces.Select(i => i.ToDisplayString()).ToList()
                : null;
        }

        return new SymbolDto(
            Name: symbol.Name,
            FullyQualifiedName: symbol.ToDisplayString(),
            Kind: symbol.Kind.ToString(),
            ContainingType: symbol.ContainingType?.ToDisplayString(),
            Namespace: symbol.ContainingNamespace?.IsGlobalNamespace == false
                ? symbol.ContainingNamespace.ToDisplayString()
                : null,
            Project: null,
            FilePath: lineSpan?.Path,
            StartLine: lineSpan?.StartLinePosition.Line + 1,
            StartColumn: lineSpan?.StartLinePosition.Character + 1,
            EndLine: lineSpan?.EndLinePosition.Line + 1,
            EndColumn: lineSpan?.EndLinePosition.Character + 1,
            ReturnType: returnType,
            Parameters: parameters,
            Modifiers: modifiers.Count > 0 ? modifiers : null,
            BaseTypes: baseTypes,
            Interfaces: interfaces,
            Documentation: symbol.GetDocumentationCommentXml());
    }

    public static LocationDto ToLocationDto(Location location, ISymbol? containingSymbol = null, string? previewText = null)
    {
        var lineSpan = location.GetLineSpan();
        return new LocationDto(
            FilePath: lineSpan.Path,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            ContainingMember: containingSymbol?.ToDisplayString(),
            PreviewText: previewText);
    }

    public static DiagnosticDto ToDiagnosticDto(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        return new DiagnosticDto(
            Id: diagnostic.Id,
            Message: diagnostic.GetMessage(),
            Severity: diagnostic.Severity.ToString(),
            Category: diagnostic.Descriptor.Category,
            FilePath: lineSpan.Path,
            StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
            StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
            EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
            EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null);
    }

    private static List<string> GetModifiers(ISymbol symbol)
    {
        var modifiers = new List<string>();
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (symbol.IsSealed) modifiers.Add("sealed");
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsExtern) modifiers.Add("extern");

        modifiers.Add(symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "unknown"
        });

        return modifiers;
    }
}
