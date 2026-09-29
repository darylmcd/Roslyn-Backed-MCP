using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Loads filesystem XML paths without passing Windows volume GUID paths through URI parsing.
/// </summary>
internal static class XmlFileLoader
{
    public static XDocument Load(string path, LoadOptions options = LoadOptions.None)
    {
        using var stream = File.OpenRead(path);
        return XDocument.Load(stream, options);
    }
}
