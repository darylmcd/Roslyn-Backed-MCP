namespace RoslynMcp.Core.Models;

/// <summary>
/// Captures the execution details and output of an external command.
/// </summary>
/// <param name="EarlyKillReason">
/// Item 4: populated when the runner terminated the process early because an
/// <c>EarlyKillPattern</c> matched stdout or stderr (typically an MSBuild MSB3027/MSB3021
/// file-lock line). <see langword="null"/> on normal termination.
/// </param>
public sealed record CommandExecutionDto(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string TargetPath,
    int ExitCode,
    bool Succeeded,
    long DurationMs,
    string StdOut,
    string StdErr,
    string? EarlyKillReason = null);
