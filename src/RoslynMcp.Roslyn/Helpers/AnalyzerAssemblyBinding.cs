using System.Reflection;
using System.Reflection.Metadata;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Pure managed assembly identity; binding never probes source or shadow files.</summary>
internal static class AnalyzerAssemblyBinding
{
    internal static string DefinitionIdentity(MetadataReader reader)
    {
        var definition = reader.GetAssemblyDefinition();
        var identity = new AssemblyName
        {
            Name = reader.GetString(definition.Name),
            Version = definition.Version,
            CultureName = definition.Culture.IsNil ? string.Empty : reader.GetString(definition.Culture),
            Flags = (AssemblyNameFlags)((int)definition.Flags & ~0xE00),
            ContentType = (AssemblyContentType)(((int)definition.Flags & 0xE00) >> 9)
        };
        var key = reader.GetBlobBytes(definition.PublicKey);
        if ((definition.Flags & AssemblyFlags.PublicKey) != 0) identity.SetPublicKey(key);
        else identity.SetPublicKeyToken(key);
        return identity.FullName ?? throw new BadImageFormatException("An analyzer has no managed assembly identity.");
    }

    // ALC accepts an already-loaded version at least as high as the request.
    // Unspecified request fields remain unspecified; supplied culture/key remain binding constraints.
    internal static bool Compatible(AssemblyName requested, AssemblyName candidate)
        => string.Equals(requested.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
            && (requested.Version is null || candidate.Version is { } actualVersion && actualVersion >= requested.Version)
            && (requested.CultureName is null || string.Equals(requested.CultureName, candidate.CultureName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            && (requested.GetPublicKeyToken() is not { } token || token.AsSpan().SequenceEqual(candidate.GetPublicKeyToken() ?? []))
            && requested.ContentType == candidate.ContentType;

    internal static int Select(AssemblyName requested, IReadOnlyList<AssemblyName> candidates)
    {
        var compatible = Enumerable.Range(0, candidates.Count).Where(index => Compatible(requested, candidates[index])).ToArray();
        var exact = compatible.Where(index => string.Equals(requested.FullName, candidates[index].FullName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = exact.Length > 0 ? exact : compatible;
        return selected.Length switch
        {
            0 => -1,
            1 => selected[0],
            _ => throw new FileLoadException("Multiple admitted analyzer dependencies satisfy the requested assembly identity.")
        };
    }
}
