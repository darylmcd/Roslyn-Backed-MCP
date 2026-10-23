using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

/// <summary>A passing TRX fragment cannot override a failed or interrupted command.</summary>
internal static class TestExecutionVerdict
{
    internal static string Status(TestRunResultDto result)
    {
        if (result.FailureEnvelope?.ErrorKind == "Timeout") return "timeout";
        if (result.Failed > 0 || result.Failures.Count != 0) return "test-failure";
        // Preserve the established zero-run observation independently of its cause.
        if (result.Total == 0) return "test-zero-run";
        if (result.Total < 0 || result.Passed < 0 || result.Failed < 0 || result.Skipped < 0
            || result.Passed > result.Total || result.Failed > result.Total || result.Skipped > result.Total
            || result.Passed > result.Total - result.Skipped
            || result.Passed == 0 && result.Skipped == 0 || !result.Execution.Succeeded || result.Execution.ExitCode != 0
            || result.Execution.EarlyKillReason is not null || result.FailureEnvelope is not null)
            return "test-failure";
        return "clean";
    }
}
