namespace RoslynMcp.Tests.Helpers;

/// <summary>Explicit native fixture configuration, replayed by the production captured-global contract.</summary>
internal static class OwnedNativeLoadContext
{
    internal static Dictionary<string, string> Properties(string loadedPath, IReadOnlyDictionary<string, string> overrides)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(loadedPath))
            ?? throw new AssertFailedException("The native fixture needs an owned physical directory.");
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildUserExtensionsPath"] = Path.Combine(directory, ".roslynmcp", "fixture-user-extensions")
        };
        foreach (var property in overrides) properties[property.Key] = property.Value;
        return properties;
    }
}
