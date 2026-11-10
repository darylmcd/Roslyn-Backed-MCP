using System.Text.RegularExpressions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Allowlist projections for the command boundary records. The runner and gate see
/// admission-augmented arguments (caller <c>--filter</c> text, appended <c>-p:</c> execution roots)
/// and absolute target paths; boundary records may carry only these coarse, non-identifying values.
/// </summary>
internal static partial class CommandBoundaryTelemetry
{
    [GeneratedRegex("^-{0,2}[a-z][a-z0-9-]{0,31}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VerbShape();

    /// <summary>The first argument when it is a bare dotnet verb or option name; never a path, value or caller text.</summary>
    public static string Verb(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return "none";
        }

        return arguments[0] is { } first && VerbShape().IsMatch(first) ? first : "other";
    }

    /// <summary>Classifies the command target as solution, project, directory or other without naming it.</summary>
    public static string TargetKind(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))
        {
            return "other";
        }

        var extension = Path.GetExtension(targetPath);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return "solution";
        }

        if (extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
        {
            return "project";
        }

        return Directory.Exists(targetPath) ? "directory" : "other";
    }
}
