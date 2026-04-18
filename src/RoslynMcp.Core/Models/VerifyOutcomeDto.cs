namespace RoslynMcp.Core.Models;

/// <summary>
/// Outcome of the post-edit verify pass attached to <c>apply_text_edit</c> /
/// <c>apply_multi_file_edit</c> when callers pass <c>verify=true</c>.
/// </summary>
/// <remarks>
/// The verify pass runs <c>compile_check</c> scoped to the owning project(s) of the
/// edited file(s) AFTER the edit is applied and persisted to disk, then compares the
/// resulting error diagnostics against a pre-edit baseline so pre-existing compile
/// errors are not attributed to the current call.
/// <para>
/// Status values:
/// <list type="bullet">
///   <item><c>skipped</c> — caller did not request verification (default path).</item>
///   <item><c>clean</c> — verify ran and introduced no new errors.</item>
///   <item><c>errors_introduced</c> — verify ran, new errors appeared, and the
///     caller did NOT request <c>autoRevertOnError</c>, so the workspace stays
///     in the errored state for inspection.</item>
///   <item><c>reverted</c> — verify ran, new errors appeared, and
///     <c>autoRevertOnError=true</c> triggered a successful single-shot revert
///     via the existing <c>revert_last_apply</c> slot.</item>
///   <item><c>revert_failed</c> — verify ran, new errors appeared, revert was
///     attempted but the undo stack could not restore the pre-edit state. The
///     workspace is in an inconsistent state and requires manual inspection.</item>
/// </list>
/// </para>
/// </remarks>
public sealed record VerifyOutcomeDto(
    string Status,
    int PreErrorCount,
    int PostErrorCount,
    IReadOnlyList<DiagnosticDto> NewDiagnostics,
    string? ProjectFilter = null,
    string? Message = null);
