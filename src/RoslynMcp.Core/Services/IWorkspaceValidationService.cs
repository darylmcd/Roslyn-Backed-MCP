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
    /// <see cref="ValidateAsync"/>. Scopes the bundle to the touched-file set so post-edit verify
    /// runs against only the projects that own the modified files. When <c>git</c> is unavailable
    /// or the solution is not inside a git repository, falls back to full-workspace validation
    /// (<c>changedFilePaths=null</c>) and surfaces the fallback via
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
/// <param name="OverallStatus">One of <c>clean</c>, <c>compile-error</c>, <c>analyzer-error</c>, <c>test-failure</c>, <c>skipped</c>.</param>
/// <param name="ChangedFilePaths">Caller-supplied paths that actually resolved to a document in the workspace. Drives compile-check scoping + test discovery.</param>
/// <param name="UnknownFilePaths">
/// dr-9-8-bug-validate-fabricated-accepts-fabricated-silen — caller-supplied paths that did NOT
/// resolve to any workspace document. Pre-fix these were silently dropped inside
/// <c>FindRelatedTestsForFilesAsync</c> and the response gave no indication that part of the
/// requested scope was ignored. Non-null; empty list when all paths resolved or when the change
/// tracker supplied the path set (in which case they are guaranteed to exist).
/// </param>
/// <param name="CompileResult">Result of the compile-check stage.</param>
/// <param name="ErrorDiagnostics">All compiler/analyzer diagnostics with severity <c>Error</c> across the validated scope.</param>
/// <param name="WarningCount">Count of warning-severity diagnostics in scope (not surfaced individually to keep response size bounded).</param>
/// <param name="DiscoveredTests">Test cases discovered for the changed files; empty list when no related tests were found.</param>
/// <param name="DotnetTestFilter">The combined <c>dotnet test --filter</c> expression to re-run just the related tests; <see langword="null"/> when none.</param>
/// <param name="TestRunResult">Populated only when <c>runTests=true</c>; otherwise <see langword="null"/>.</param>
/// <param name="Warnings">
/// post-edit-validate-workspace-scoped-to-touched-files: non-fatal diagnostics surfaced to the
/// caller. Used by <see cref="IWorkspaceValidationService.ValidateRecentGitChangesAsync"/> to
/// signal fallbacks (missing <c>git</c> on PATH, solution outside a git repo, git exited with
/// an error) that caused the bundle to validate the full workspace instead of the scoped
/// touched-file set. Non-null; empty list when validation ran as requested.
/// </param>
public sealed record WorkspaceValidationDto(
    string OverallStatus,
    IReadOnlyList<string> ChangedFilePaths,
    IReadOnlyList<string> UnknownFilePaths,
    CompileCheckDto CompileResult,
    IReadOnlyList<DiagnosticDto> ErrorDiagnostics,
    int WarningCount,
    IReadOnlyList<RelatedTestCaseDto> DiscoveredTests,
    string? DotnetTestFilter,
    TestRunResultDto? TestRunResult,
    IReadOnlyList<string> Warnings);
