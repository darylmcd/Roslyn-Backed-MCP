using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Applies requested match semantics to the final output modeled by canned command runners.</summary>
internal static class CommandRunnerTestResults
{
    internal static CommandExecutionDto ApplyEarlyKill(CommandExecutionDto execution, IReadOnlyList<EarlyKillPattern>? patterns)
    {
        if (execution.EarlyKillReason is not null || patterns is null) return execution;
        var match = patterns.FirstOrDefault(pattern => pattern.Pattern.IsMatch(execution.StdOut) || pattern.Pattern.IsMatch(execution.StdErr));
        return match is null ? execution : execution with { Succeeded = false, EarlyKillReason = match.Reason };
    }
}
