namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of collecting test coverage data.
/// </summary>
public sealed record TestCoverageResultDto(
    bool Success,
    string? Error,
    double? LineCoveragePercent,
    double? BranchCoveragePercent,
    IReadOnlyList<ModuleCoverageDto> Modules,
    TestCoverageFailureEnvelopeDto? FailureEnvelope = null);

/// <summary>
/// Structured failure envelope for test coverage operations, enabling programmatic
/// detection of failure conditions (e.g., missing coverlet package).
/// </summary>
/// <param name="ErrorKind">Machine-readable bucket: <c>CoverletMissing</c>, <c>TestFailure</c>, <c>Unknown</c>.</param>
/// <param name="IsRetryable">True when the caller can retry after fixing a transient condition.</param>
/// <param name="Summary">Human-readable summary.</param>
/// <param name="MissingPackages">
/// test-coverage-vague-error-when-coverlet-missing: when <see cref="ErrorKind"/> is
/// <c>CoverletMissing</c>, this lists the test projects that don't reference
/// <c>coverlet.collector</c>. Empty for other error kinds.
/// </param>
public sealed record TestCoverageFailureEnvelopeDto(
    string ErrorKind,
    bool IsRetryable,
    string Summary,
    IReadOnlyList<string>? MissingPackages = null);

/// <summary>
/// Represents coverage information for a single module.
/// </summary>
public sealed record ModuleCoverageDto(
    string ModuleName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal,
    IReadOnlyList<ClassCoverageDto>? Classes);

/// <summary>
/// Represents coverage information for a single class.
/// </summary>
public sealed record ClassCoverageDto(
    string ClassName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal);
