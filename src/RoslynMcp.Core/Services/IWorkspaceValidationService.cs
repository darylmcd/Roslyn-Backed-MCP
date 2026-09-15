using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 5 (v1.18, <c>roslyn-mcp-post-edit-validation-bundle</c>): chains the four primitives an
/// agent typically calls after an edit (compile_check + project_diagnostics +
/// test_related_files + optional test_run) into a single composite call. Reduces 4 round-trips
/// to 1 and lets the response surface the overall pass/fail status under one name.
/// </summary>
public interface IWorkspaceValidationService
{
    /// <summary>Compile and collect diagnostics across the whole workspace, then discover related tests for the resolved changed files.</summary>
    /// <param name="changedFilePaths">Paths used for related-test discovery, not compiler or diagnostic filtering. Null or empty uses the change tracker, reconciled with Git when available.</param>
    /// <param name="summary">
    /// (validate-workspace-output-cap-summary-mode) When <c>true</c>, drops the per-diagnostic
    /// <see cref="WorkspaceValidationDto.ErrorDiagnostics"/> list and per-test
    /// <see cref="WorkspaceValidationDto.DiscoveredTests"/> list to keep the response under the
    /// MCP cap on multi-project solutions. The aggregated counts +
    /// <see cref="WorkspaceValidationDto.OverallStatus"/> still surface the verdict; callers
    /// wanting per-item detail should re-run with <c>summary=false</c> or call the underlying
    /// primitive (<c>project_diagnostics</c>, <c>test_related_files</c>) directly. Default
    /// <c>false</c> preserves the v1.18 response shape.
    /// </param>
    Task<WorkspaceValidationDto> ValidateAsync(
        string workspaceId,
        IReadOnlyList<string>? changedFilePaths,
        bool runTests,
        CancellationToken ct,
        bool summary = false);

    /// <summary>
    /// post-edit-validate-workspace-scoped-to-touched-files: auto-derives <c>changedFilePaths</c>
    /// from <c>git status --porcelain</c> in the solution directory and forwards to
    /// <see cref="ValidateAsync"/>. The resolved touched files scope related-test discovery;
    /// compilation and diagnostic collection always cover the whole workspace. When <c>git</c>
    /// is unavailable or the solution is outside a Git repository, test discovery uses the
    /// change-tracker fallback (<c>changedFilePaths=null</c>) and surfaces the fallback via
    /// <see cref="WorkspaceValidationDto.Warnings"/>.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by <c>workspace_load</c>.</param>
    /// <param name="runTests">When <see langword="true"/>, runs the discovered related tests via <c>dotnet test --filter</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="summary">See <see cref="ValidateAsync"/>.</param>
    Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(
        string workspaceId,
        bool runTests,
        CancellationToken ct,
        bool summary = false);
}

/// <summary>
/// Aggregated output of <see cref="IWorkspaceValidationService.ValidateAsync"/>.
/// </summary>
/// <param name="OverallStatus">One of <c>clean</c>, <c>compile-error</c>, <c>analyzer-error</c>, <c>test-failure</c>, <c>test-zero-run</c>, <c>git-status-unknown</c>, <c>timeout</c>, <c>compile-incomplete</c>. <c>compile-incomplete</c> means compilation was cancelled or fewer projects completed than were selected, with no higher-priority diagnostic or test failure. Retry compilation before treating validation as passing. <c>analyzer-error</c> covers retained Error-severity diagnostics beyond the authoritative compiler error count, including analyzer/workspace errors and uncorroborated compiler-category errors when compilation is incomplete. <c>test-zero-run</c> (added in validate-workspace-runtests-total-zero) indicates <c>runTests=true</c> produced <c>testRunResult.Total=0</c> with no reported test failures and a non-empty discovered filter. No tests were reported; investigate with the same filter before treating the run as passing. <c>git-status-unknown</c> (added in validate-recent-git-changes-status-timeout-false-clean) is emitted only by <see cref="IWorkspaceValidationService.ValidateRecentGitChangesAsync"/> when the <c>git status</c> collection exceeded its own timeout: the verdict would otherwise have been <c>clean</c>, but it was computed over a change-tracker fallback scope rather than the real working tree, so it cannot be distinguished from a genuinely clean tree — re-run (the warning carries <c>retryable=true</c>) or raise <c>ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS</c>. <c>timeout</c> is emitted when an internal validation phase exceeds its own timeout: <see cref="CompileCheckDto.Cancelled"/> is <see langword="true"/> and the synthetic test-run failure envelope reports an <c>ErrorKind</c> of <c>Timeout</c> even when <c>runTests=false</c>. Callers exact-matching <c>"clean"</c> as the only passing verdict need to treat <c>"test-zero-run"</c>, <c>"git-status-unknown"</c>, and <c>"timeout"</c> as non-passing verdicts too.</param>
/// <param name="ChangedFilePaths">Resolved caller-supplied or tracker-derived paths used for related-test discovery, retained on phase timeout. Compilation and diagnostic collection cover the whole workspace.</param>
/// <param name="UnknownFilePaths">
/// dr-9-8-bug-validate-fabricated-accepts-fabricated-silen — caller-supplied paths that did NOT
/// resolve to any workspace document. Pre-fix these were silently dropped inside
/// <c>FindRelatedTestsForFilesAsync</c> and the response gave no indication that part of the
/// requested scope was ignored. Non-null; empty list when all paths resolved or when the change
/// tracker supplied the path set (in which case they are guaranteed to exist).
/// </param>
/// <param name="CompileResult">Result of the compile-check stage.</param>
/// <param name="ErrorDiagnostics">
/// All compiler/analyzer diagnostics with severity <c>Error</c> across the whole workspace.
/// validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics: when
/// <c>summary=true</c> on <see cref="IWorkspaceValidationService.ValidateAsync"/> this list is
/// dropped to keep the response under the MCP cap — use <see cref="ErrorCount"/> to recover
/// the number of errors that drove the verdict.
/// </param>
/// <param name="ErrorCount">
/// validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics: count of
/// error-severity compiler/analyzer diagnostics across the whole workspace. Always populated (mirrors the
/// existing <see cref="WarningCount"/> pattern) so callers can see how many errors drove
/// the <see cref="OverallStatus"/> verdict even when <c>summary=true</c> suppressed
/// <see cref="ErrorDiagnostics"/>.
/// </param>
/// <param name="WarningCount">Whole-workspace compile-check warning count (not surfaced individually to keep response size bounded).</param>
/// <param name="DiscoveredTests">Test cases discovered for the changed files; empty list when no related tests were found.</param>
/// <param name="DotnetTestFilter">The combined <c>dotnet test --filter</c> expression to re-run just the related tests; <see langword="null"/> when none.</param>
/// <param name="TestRunResult">Populated when related tests execute, or with a synthetic failure envelope on validation-phase timeout even when <c>runTests=false</c>; otherwise <see langword="null"/>.</param>
/// <param name="Warnings">
/// post-edit-validate-workspace-scoped-to-touched-files: non-fatal diagnostics surfaced to the
/// caller. Used by <see cref="IWorkspaceValidationService.ValidateRecentGitChangesAsync"/> to
/// signal fallbacks (missing <c>git</c> on PATH, solution outside a git repo, git exited with
/// an error) that caused related-test discovery to use the change-tracker fallback.
/// Compilation and diagnostic collection always cover the whole workspace. Non-null; empty list when validation ran as requested.
/// </param>
public sealed record WorkspaceValidationDto(
    string OverallStatus,
    IReadOnlyList<string> ChangedFilePaths,
    IReadOnlyList<string> UnknownFilePaths,
    CompileCheckDto CompileResult,
    IReadOnlyList<DiagnosticDto> ErrorDiagnostics,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<RelatedTestCaseDto> DiscoveredTests,
    string? DotnetTestFilter,
    TestRunResultDto? TestRunResult,
    IReadOnlyList<string> Warnings);
