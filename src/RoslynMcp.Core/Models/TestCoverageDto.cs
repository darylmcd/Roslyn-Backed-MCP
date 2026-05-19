namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of collecting test coverage data.
/// </summary>
/// <param name="CoverageGaps">
/// test-coverage-fail-fast-on-missing-coverlet: when a subset of in-scope test projects lack
/// the <c>coverlet.collector</c> NuGet package, the tool now runs per-project coverage on the
/// projects that DO have the collector and lists the skipped project names here so the caller
/// can report partial coverage with <see cref="Success"/>=<c>true</c> instead of failing the
/// entire call. <see langword="null"/> when every in-scope test project has coverlet (the
/// classic happy path) and when the fail-fast path triggers (every project lacks coverlet —
/// the <see cref="FailureEnvelope"/> carries the project list in that case).
/// </param>
public sealed record TestCoverageResultDto(
    bool Success,
    string? Error,
    double? LineCoveragePercent,
    double? BranchCoveragePercent,
    IReadOnlyList<ModuleCoverageDto> Modules,
    TestCoverageFailureEnvelopeDto? FailureEnvelope = null,
    IReadOnlyList<string>? CoverageGaps = null);

/// <summary>
/// Structured failure envelope for test coverage operations, enabling programmatic
/// detection of failure conditions (e.g., missing coverlet package).
/// </summary>
/// <param name="ErrorKind">Machine-readable bucket: <c>CoverletMissing</c>, <c>TestFailure</c>, <c>Timeout</c>, <c>Unknown</c>.</param>
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
