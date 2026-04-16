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
}

/// <summary>
/// Aggregated output of <see cref="IWorkspaceValidationService.ValidateAsync"/>.
/// </summary>
/// <param name="OverallStatus">One of <c>clean</c>, <c>compile-error</c>, <c>analyzer-error</c>, <c>test-failure</c>, <c>skipped</c>.</param>
/// <param name="ChangedFilePaths">The file set that drove the validation (caller-supplied or derived from <c>workspace_changes</c>).</param>
/// <param name="CompileResult">Result of the compile-check stage.</param>
/// <param name="ErrorDiagnostics">All compiler/analyzer diagnostics with severity <c>Error</c> across the validated scope.</param>
/// <param name="WarningCount">Count of warning-severity diagnostics in scope (not surfaced individually to keep response size bounded).</param>
/// <param name="DiscoveredTests">Test cases discovered for the changed files; empty list when no related tests were found.</param>
/// <param name="DotnetTestFilter">The combined <c>dotnet test --filter</c> expression to re-run just the related tests; <see langword="null"/> when none.</param>
/// <param name="TestRunResult">Populated only when <c>runTests=true</c>; otherwise <see langword="null"/>.</param>
public sealed record WorkspaceValidationDto(
    string OverallStatus,
    IReadOnlyList<string> ChangedFilePaths,
    CompileCheckDto CompileResult,
    IReadOnlyList<DiagnosticDto> ErrorDiagnostics,
    int WarningCount,
    IReadOnlyList<RelatedTestCaseDto> DiscoveredTests,
    string? DotnetTestFilter,
    TestRunResultDto? TestRunResult);
