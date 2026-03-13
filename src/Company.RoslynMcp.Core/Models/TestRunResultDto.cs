namespace Company.RoslynMcp.Core.Models;

public sealed record TestRunResultDto(
    CommandExecutionDto Execution,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestFailureDto> Failures);

public sealed record TestFailureDto(
    string DisplayName,
    string? FullyQualifiedName,
    string Message,
    string? StackTrace);
