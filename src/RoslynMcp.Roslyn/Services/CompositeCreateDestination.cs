using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class CompositeCreateDestination
{
    internal static bool IsAvailable(string path)
    {
        try
        {
            return IsPhysicalPath(path) && !EntryExists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    internal static void RequireAvailableForPreview(string path)
    {
        if (!IsAvailable(path))
            throw new PublicInvalidOperationException(
                "A composite preview requires an unused physical path for each new file. Choose a different destination and re-issue the preview.");
    }

    internal static bool IsPhysicalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        // Resolve the raw path in filesystem component order before collapsing parent traversals.
        var physical = PhysicalPathResolver.Resolve(path);
        return FileSystemPath.Comparer.Equals(physical, Path.GetFullPath(path)) &&
               !path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Contains("..");
    }

    internal static bool EntryExists(string path)
    {
        try
        {
            // Unlike File.Exists, attributes detect directories and dangling symbolic links.
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}

internal sealed class CompositeCreateConflictException()
    : IOException("The composite create destination is occupied or resolves through a filesystem link.");
