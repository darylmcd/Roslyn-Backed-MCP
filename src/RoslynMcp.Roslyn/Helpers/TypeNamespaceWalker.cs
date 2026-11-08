using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class TypeNamespaceWalker
{
    public static void Collect(HashSet<string> namespaces, ITypeSymbol? type, string? ownNamespace)
    {
        foreach (var referencedType in Enumerate(type))
        {
            var typeNamespace = referencedType.ContainingNamespace;
            if (typeNamespace is { IsGlobalNamespace: false })
            {
                var nsName = typeNamespace.ToDisplayString();
                if (!string.Equals(nsName, ownNamespace, StringComparison.Ordinal))
                {
                    namespaces.Add(nsName);
                }
            }
        }
    }

    public static IEnumerable<ITypeSymbol> Enumerate(ITypeSymbol? type)
    {
        if (type is null or ITypeParameterSymbol)
        {
            yield break;
        }

        yield return type;

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments)
            {
                foreach (var nested in Enumerate(argument)) yield return nested;
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            foreach (var nested in Enumerate(array.ElementType)) yield return nested;
        }

        if (type is IPointerTypeSymbol pointer)
        {
            foreach (var nested in Enumerate(pointer.PointedAtType)) yield return nested;
        }

        if (type is IFunctionPointerTypeSymbol functionPointer)
        {
            foreach (var nested in Enumerate(functionPointer.Signature.ReturnType)) yield return nested;
            foreach (var parameter in functionPointer.Signature.Parameters)
            {
                foreach (var nested in Enumerate(parameter.Type)) yield return nested;
            }
        }

        // `Outer<string>.Inner<object>` refers to `Outer<string>` too: its type arguments need their imports.
        if (type.ContainingType is { } containing)
        {
            foreach (var nested in Enumerate(containing)) yield return nested;
        }
    }
}
