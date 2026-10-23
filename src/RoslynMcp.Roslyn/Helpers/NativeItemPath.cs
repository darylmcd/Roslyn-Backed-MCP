using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Coordinates consumed as native item FullPath metadata, from decoded evaluated values.</summary>
internal static class NativeItemPath
{
    internal static bool IsPathFullyQualified(string value) => Path.IsPathFullyQualified(Interpret(value));

    private static string Interpret(string value) => new TaskItem(ProjectCollection.Escape(value)).ItemSpec;

    internal static string FullPath(string evaluatedValue, string projectDirectory)
    {
        // These operands have already been unescaped by evaluation. Escape only for
        // TaskItem transport, preserving literal percent sequences. Interpret separators
        // before rootedness: a leading backslash is a native rooted path on Unix.
        var interpreted = Interpret(evaluatedValue);
        var absolute = Path.GetFullPath(interpreted, projectDirectory);
        return new TaskItem(ProjectCollection.Escape(absolute)).GetMetadata("FullPath");
    }
}
