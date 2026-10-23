using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Copies the installed-package fixture's native project into an already issued private owner.</summary>
internal static class NativePackageForkCopy
{
    internal static string Copy(string payloadRoot, PhysicalDirectoryScope.OwnedRoot owner,
        PhysicalGraphLayout layout, string sourceLoadedPath)
    {
        owner.AssertCurrentPath(CancellationToken.None);
        var project = layout.MapTo(owner.Path, sourceLoadedPath);
        var destination = Path.GetDirectoryName(project) ?? throw new AssertFailedException("The private project requires a directory.");
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "Owner.csproj", "global.json", "Directory.Build.props" })
            File.Copy(Path.Combine(payloadRoot, name), Path.Combine(destination, name));
        Directory.CreateDirectory(Path.Combine(destination, "obj"));
        foreach (var generated in Directory.EnumerateFiles(Path.Combine(payloadRoot, "obj"), "*.nuget.g.*"))
            File.Copy(generated, Path.Combine(destination, "obj", Path.GetFileName(generated)));
        owner.AssertCurrentPath(CancellationToken.None);
        return project;
    }
}
