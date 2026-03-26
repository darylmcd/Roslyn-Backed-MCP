using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Maps Roslyn <see cref="ISymbol"/> instances and related types to their DTO representations.
/// </summary>
public static class SymbolMapper
{
    /// <summary>
    /// Maps a Roslyn symbol to a <see cref="SymbolDto"/>, optionally resolving the project name
    /// from the given solution and overriding the primary source location.
    /// </summary>
    /// <param name="symbol">The symbol to map.</param>
    /// <param name="solution">The solution used to resolve the owning project, or <see langword="null"/> to skip project resolution.</param>
    /// <param name="primaryLocation">An explicit source location to use, or <see langword="null"/> to pick the first source location from <paramref name="symbol"/>.</param>
    public static SymbolDto ToDto(ISymbol symbol, Solution? solution = null, Location? primaryLocation = null)
    {
        var location = primaryLocation ?? symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var lineSpan = location?.GetLineSpan();
        var kind = GetKind(symbol);
        var projectName = location?.SourceTree is not null && solution is not null
            ? solution.GetDocument(location.SourceTree)?.Project.Name
            : null;
        var symbolHandle = symbol.Locations.Any(static location => location.IsInSource) || symbol is INamedTypeSymbol
            ? SymbolHandleSerializer.CreateHandle(symbol)
            : null;

        var returnType = GetReturnType(symbol);
        var parameters = GetParameters(symbol);
        var modifiers = GetModifiers(symbol);
        var (baseTypes, interfaces) = GetTypeHierarchy(symbol);
        var (hasGetter, hasSetter, setterAccessibility) = GetPropertyAccessors(symbol);

        return new SymbolDto(
            Name: symbol.Name,
            FullyQualifiedName: symbol.ToDisplayString(),
            SymbolHandle: symbolHandle,
            Kind: kind,
            ContainingType: symbol.ContainingType?.ToDisplayString(),
            Namespace: symbol.ContainingNamespace?.IsGlobalNamespace == false
                ? symbol.ContainingNamespace.ToDisplayString()
                : null,
            Project: projectName,
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
            Documentation: symbol.GetDocumentationCommentXml(),
            HasGetter: hasGetter,
            HasSetter: hasSetter,
            SetterAccessibility: setterAccessibility);
    }

    private static string? GetReturnType(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m => m.ReturnType.ToDisplayString(),
        IPropertySymbol p => p.Type.ToDisplayString(),
        IFieldSymbol f => f.Type.ToDisplayString(),
        IEventSymbol e => e.Type.ToDisplayString(),
        _ => null
    };

    private static List<string>? GetParameters(ISymbol symbol) =>
        symbol is IMethodSymbol method
            ? method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}").ToList()
            : null;

    private static (IReadOnlyList<string>? BaseTypes, IReadOnlyList<string>? Interfaces) GetTypeHierarchy(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol namedType)
            return (null, null);

        var baseTypes = namedType.BaseType is not null && namedType.BaseType.SpecialType != SpecialType.System_Object
            ? [namedType.BaseType.ToDisplayString()]
            : (IReadOnlyList<string>?)null;
        var interfaces = namedType.Interfaces.Length > 0
            ? namedType.Interfaces.Select(i => i.ToDisplayString()).ToList()
            : (IReadOnlyList<string>?)null;
        return (baseTypes, interfaces);
    }

    private static (bool? HasGetter, bool? HasSetter, string? SetterAccessibility) GetPropertyAccessors(ISymbol symbol)
    {
        if (symbol is not IPropertySymbol prop)
            return (null, null, null);

        string? setterAccessibility = null;
        if (prop.SetMethod is not null)
        {
            setterAccessibility = prop.SetMethod.IsInitOnly ? "init" : prop.SetMethod.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => "unknown"
            };
        }

        return (prop.GetMethod is not null, prop.SetMethod is not null, setterAccessibility);
    }

    /// <summary>
    /// Maps a Roslyn <see cref="Location"/> to a <see cref="LocationDto"/>.
    /// </summary>
    /// <param name="location">The source location to map.</param>
    /// <param name="containingSymbol">The symbol that contains this location, used to populate <see cref="LocationDto.ContainingMember"/>.</param>
    /// <param name="previewText">A short preview of the source text at the location, or <see langword="null"/>.</param>
    /// <param name="classification">An optional classification string (e.g., <c>Read</c>, <c>Write</c>).</param>
    public static LocationDto ToLocationDto(Location location, ISymbol? containingSymbol = null, string? previewText = null, string? classification = null)
    {
        var lineSpan = location.GetLineSpan();
        return new LocationDto(
            FilePath: lineSpan.Path,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            ContainingMember: containingSymbol?.ToDisplayString(),
            PreviewText: previewText,
            Classification: classification);
    }

    /// <summary>
    /// Classifies a reference location as <c>Read</c>, <c>Write</c>, <c>ReadWrite</c>, <c>NameOf</c>,
    /// <c>Attribute</c>, or <c>Other</c> based on the surrounding syntax context.
    /// </summary>
    private static readonly HashSet<SyntaxKind> CompoundAssignmentKinds =
    [
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.DivideAssignmentExpression,
    ];

    public static string ClassifyReferenceLocation(ReferenceLocation refLocation)
    {
        if (refLocation.IsImplicit)
            return "Other";

        var syntaxNode = refLocation.Location.SourceTree?
            .GetRoot()
            .FindNode(refLocation.Location.SourceSpan);

        if (syntaxNode is null)
            return "Read";

        if (IsNameOfContext(syntaxNode))
            return "NameOf";

        if (syntaxNode.Ancestors().OfType<AttributeSyntax>().Any())
            return "Attribute";

        return ClassifyByParentSyntax(syntaxNode);
    }

    private static bool IsNameOfContext(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof");

    private static string ClassifyByParentSyntax(SyntaxNode node)
    {
        switch (node.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == node:
                return CompoundAssignmentKinds.Contains(assignment.Kind()) ? "ReadWrite" : "Write";

            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.Parent is AssignmentExpressionSyntax memberAssignment
                     && memberAssignment.Left == memberAccess:
                return "Write";

            case PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                return "ReadWrite";

            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                return "ReadWrite";

            case ArgumentSyntax arg
                when arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword):
                return "Write";

            default:
                return "Read";
        }
    }

    /// <summary>
    /// Maps a Roslyn <see cref="Diagnostic"/> to a <see cref="DiagnosticDto"/>.
    /// </summary>
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

    private static string GetKind(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeKind.ToString(),
            _ => symbol.Kind.ToString()
        };
}
