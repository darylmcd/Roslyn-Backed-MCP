using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class TypeNamespaceWalker
{
    public static void Collect(HashSet<string> namespaces, ITypeSymbol? type, string? ownNamespace)
    {
        if (type is null)
        {
            return;
        }

        if (type is ITypeParameterSymbol)
        {
            return;
        }

        var typeNamespace = type.ContainingNamespace;
        if (typeNamespace is { IsGlobalNamespace: false })
        {
            var nsName = typeNamespace.ToDisplayString();
            if (!string.Equals(nsName, ownNamespace, StringComparison.Ordinal))
            {
                namespaces.Add(nsName);
            }
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments)
            {
                Collect(namespaces, argument, ownNamespace);
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            Collect(namespaces, array.ElementType, ownNamespace);
        }

        if (type is IPointerTypeSymbol pointer)
        {
            Collect(namespaces, pointer.PointedAtType, ownNamespace);
        }

        if (type is IFunctionPointerTypeSymbol functionPointer)
        {
            Collect(namespaces, functionPointer.Signature.ReturnType, ownNamespace);
            foreach (var parameter in functionPointer.Signature.Parameters)
            {
                Collect(namespaces, parameter.Type, ownNamespace);
            }
        }

        // `Outer<string>.Inner<object>` refers to `Outer<string>` too: its type arguments need their imports.
        if (type.ContainingType is { } containing)
        {
            Collect(namespaces, containing, ownNamespace);
        }
    }
}
