namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of executing a build-related command together with reported diagnostics.
/// </summary>
public sealed record BuildResultDto(
    CommandExecutionDto Execution,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int ErrorCount,
    int WarningCount);
