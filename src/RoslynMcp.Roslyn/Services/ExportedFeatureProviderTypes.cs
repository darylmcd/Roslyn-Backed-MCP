using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Finds provider exports without resolving unrelated analyzer implementation types.
/// Analyzer assemblies can embed helpers with dependencies unavailable to the host.
/// </summary>
internal static class ExportedFeatureProviderTypes
{
    internal static Type[] Read(Assembly assembly, string exportAttributeFullName)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        List<Type> providers = [];
        List<Exception> failures = [];
        foreach (var handle in metadata.TypeDefinitions)
        {
            var definition = metadata.GetTypeDefinition(handle);
            if ((definition.Attributes & TypeAttributes.Abstract) != 0
                || !definition.GetCustomAttributes().Any(attribute =>
                    GetAttributeTypeName(metadata, attribute) == exportAttributeFullName))
            {
                continue;
            }

            try
            {
                var providerType = assembly.GetType(GetTypeName(metadata, handle), throwOnError: true)
                    ?? throw new TypeLoadException("The exported provider type could not be resolved.");
                providers.Add(providerType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            // Preserve healthy exports and fail-closed completeness through the shared loader.
            throw new ReflectionTypeLoadException(providers.ToArray(), failures.ToArray());
        }

        return providers.ToArray();
    }

    private static string? GetAttributeTypeName(MetadataReader metadata, CustomAttributeHandle handle)
    {
        var constructor = metadata.GetCustomAttribute(handle).Constructor;
        var typeHandle = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default(EntityHandle),
        };
        if (typeHandle.Kind == HandleKind.TypeDefinition)
        {
            return GetTypeName(metadata, (TypeDefinitionHandle)typeHandle);
        }

        if (typeHandle.Kind != HandleKind.TypeReference) return null;
        var reference = metadata.GetTypeReference((TypeReferenceHandle)typeHandle);
        var ns = metadata.GetString(reference.Namespace);
        var name = metadata.GetString(reference.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string GetTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var definition = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil) return $"{GetTypeName(metadata, declaringType)}+{name}";
        var ns = metadata.GetString(definition.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}
