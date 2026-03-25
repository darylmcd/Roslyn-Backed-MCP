namespace RoslynMcp.Core.Models;

/// <summary>
/// Captures the execution details and output of an external command.
/// </summary>
public sealed record CommandExecutionDto(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string TargetPath,
    int ExitCode,
    bool Succeeded,
    long DurationMs,
    string StdOut,
    string StdErr);
