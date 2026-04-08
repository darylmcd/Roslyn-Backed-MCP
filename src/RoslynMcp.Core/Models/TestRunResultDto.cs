namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of executing a test run. When <paramref name="FailureEnvelope"/>
/// is non-null, the run terminated without producing structured TRX output and the
/// envelope carries a typed classification (file lock, build failure, timeout, unknown)
/// plus the tail of StdOut/StdErr so callers can decide whether to retry.
/// </summary>
public sealed record TestRunResultDto(
    CommandExecutionDto Execution,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestFailureDto> Failures,
    TestRunFailureEnvelopeDto? FailureEnvelope = null);

/// <summary>
/// Represents a failed test case from a test run.
/// </summary>
public sealed record TestFailureDto(
    string DisplayName,
    string? FullyQualifiedName,
    string Message,
    string? StackTrace);

/// <summary>
/// Structured failure envelope populated when <c>dotnet test</c> exits without producing
/// TRX output (e.g., MSBuild file locks, build failures, timeouts). Carries an
/// <see cref="ErrorKind"/> classification, a retry hint, and the tail of the captured
/// StdOut/StdErr streams so callers do not need to re-run just to see what happened.
/// </summary>
/// <param name="ErrorKind">
/// Classification of the failure. One of: <c>"FileLock"</c> (MSB3027/MSB3021),
/// <c>"BuildFailure"</c> (compilation errors prevented the test run),
/// <c>"Timeout"</c> (the invocation exceeded the configured test timeout),
/// <c>"Unknown"</c> (non-zero exit with no TRX and no recognized pattern).
/// </param>
/// <param name="IsRetryable">
/// True when a retry of the same invocation has a reasonable chance of succeeding
/// without changing source — e.g., file-lock collisions caused by another test host.
/// </param>
/// <param name="Summary">Short human-readable explanation of the detected failure mode.</param>
/// <param name="StdOutTail">
/// Last ~2000 characters of captured standard output, or <c>null</c> when nothing was captured.
/// </param>
/// <param name="StdErrTail">
/// Last ~2000 characters of captured standard error, or <c>null</c> when nothing was captured.
/// </param>
public sealed record TestRunFailureEnvelopeDto(
    string ErrorKind,
    bool IsRetryable,
    string Summary,
    string? StdOutTail,
    string? StdErrTail);
