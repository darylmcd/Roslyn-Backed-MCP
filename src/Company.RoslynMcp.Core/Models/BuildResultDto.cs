namespace Company.RoslynMcp.Core.Models;

public sealed record BuildResultDto(
    CommandExecutionDto Execution,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int ErrorCount,
    int WarningCount);
