using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Adapts explicitly supplied canned command behavior to final-output pattern matching.</summary>
internal abstract class CannedCommandRunner : IDotnetCommandRunner
{
    public IReadOnlyList<EarlyKillPattern>? RecordedEarlyKillPatterns { get; private set; }

    public abstract Task<CommandExecutionDto> RunAsync(
        string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct);

    public async Task<CommandExecutionDto> RunAsync(
        string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
    {
        RecordedEarlyKillPatterns = earlyKillPatterns;
        var execution = await RunAsync(workingDirectory, targetPath, arguments, ct).ConfigureAwait(false);
        return CommandRunnerTestResults.ApplyEarlyKill(execution, earlyKillPatterns);
    }
}
