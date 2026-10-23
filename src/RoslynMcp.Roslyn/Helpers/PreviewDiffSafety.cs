using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal static class PreviewDiffSafety
{
    internal static bool ContainsTruncation(IReadOnlyList<FileChangeDto> changes)
    {
        foreach (var change in changes)
        {
            if (string.Equals(change.FilePath, SolutionDiffHelper.TruncatedSentinelFilePath, StringComparison.Ordinal)
                || change.UnifiedDiff.Split('\n').Any(static line => line.TrimEnd('\r') == "@@ truncated @@"))
                return true;
        }
        return false;
    }

    internal static void RequireComplete(IReadOnlyList<FileChangeDto> changes)
    {
        if (ContainsTruncation(changes))
            throw new PublicInvalidOperationException(
                "The composite preview exceeds its reviewable diff limit. Narrow the operation before requesting another preview; no apply token was issued.");
    }
}
