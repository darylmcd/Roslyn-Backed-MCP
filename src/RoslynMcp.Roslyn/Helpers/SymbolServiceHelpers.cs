using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

internal static class SymbolServiceHelpers
{
    public static async Task<ISymbol?> GetContainingSymbolAsync(Document document, Location location, CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return null;

        return GetContainingSymbolFromRoot(root, semanticModel, location, ct);
    }

    public static ISymbol? GetContainingSymbolFromRoot(SyntaxNode root, SemanticModel model, Location location, CancellationToken ct)
    {
        var node = root.FindNode(location.SourceSpan);
        while (node is not null)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                return model.GetDeclaredSymbol(node, ct);
            }
            node = node.Parent;
        }
        return null;
    }

    public static async Task<IReadOnlyList<RoslynMcp.Core.Models.LocationDto>> SymbolsToLocationsAsync(
        IEnumerable<ISymbol> symbols,
        Solution solution,
        CancellationToken ct)
    {
        var results = new List<RoslynMcp.Core.Models.LocationDto>();
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

    public static IEnumerable<ISymbol> GetBaseMembers(ISymbol symbol)
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

        // Implicit interface implementations: check if this method implements any interface member
        if (method.ContainingType is not null)
        {
            foreach (var iface in method.ContainingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = method.ContainingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, method))
                        yield return member;
                }
            }
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

        // Implicit interface implementations
        if (property.ContainingType is not null)
        {
            foreach (var iface in property.ContainingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    var impl = property.ContainingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, property))
                        yield return member;
                }
            }
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

        // Implicit interface implementations
        if (eventSymbol.ContainingType is not null)
        {
            foreach (var iface in eventSymbol.ContainingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IEventSymbol>())
                {
                    var impl = eventSymbol.ContainingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, eventSymbol))
                        yield return member;
                }
            }
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
}
