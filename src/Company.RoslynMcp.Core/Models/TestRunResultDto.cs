namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of executing a test run.
/// </summary>
public sealed record TestRunResultDto(
    CommandExecutionDto Execution,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestFailureDto> Failures);

/// <summary>
/// Represents a failed test case from a test run.
/// </summary>
public sealed record TestFailureDto(
    string DisplayName,
    string? FullyQualifiedName,
    string Message,
    string? StackTrace);
