namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Filters file paths to exclude generated, NuGet content, and build output files
/// from analysis results.
/// </summary>
internal static class PathFilter
{
    private static readonly string[] ExcludedSegments =
    [
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}_content{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}_content{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.nuget{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}.nuget{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}contentFiles{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}contentFiles{Path.AltDirectorySeparatorChar}",
    ];

    /// <summary>
    /// Returns <see langword="true"/> if the file path appears to be a generated file,
    /// NuGet content file, or build output that should be excluded from analysis results.
    /// </summary>
    public static bool IsGeneratedOrContentFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return true;

        foreach (var segment in ExcludedSegments)
        {
            if (filePath.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
