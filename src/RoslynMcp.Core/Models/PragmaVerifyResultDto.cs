namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of checking whether an existing <c>#pragma warning disable/restore</c> pair
/// actually covers a diagnostic's fire site.
/// </summary>
/// <param name="Suppresses">
/// <c>true</c> when the fire line lies strictly inside a matching
/// <c>disable</c>/<c>restore</c> span for <paramref name="DiagnosticId"/>. <c>false</c>
/// when no matching pair exists, the fire line is outside the span, or the pair
/// is inverted/malformed.
/// </param>
/// <param name="FilePath">Absolute path of the file that was inspected.</param>
/// <param name="Line">The 1-based line that the caller wanted covered (the diagnostic fire site).</param>
/// <param name="DiagnosticId">The diagnostic id whose suppression was checked (e.g. <c>CA2025</c>).</param>
/// <param name="DisableLine">1-based line of the most-relevant <c>#pragma warning disable &lt;id&gt;</c>, or <c>null</c> if none was found.</param>
/// <param name="RestoreLine">
/// 1-based line of the matching <c>#pragma warning restore &lt;id&gt;</c>, or <c>null</c> if the
/// disable has no matching restore (dangling / file-end). A dangling disable is considered
/// to cover every line after it up to the end of file.
/// </param>
/// <param name="Reason">Human-readable diagnosis suitable for a terse tool response (why it does / does not cover).</param>
/// <param name="DiagnosticFiresAtLine">
/// <c>true</c> when the current in-memory compilation reports an actual diagnostic with the
/// given id at <paramref name="Line"/>, confirming the fire site is real. <c>false</c> when
/// the diagnostic does not fire there (either because it is already suppressed by the pair or
/// because the caller gave a stale line). <c>null</c> when the workspace lookup was skipped
/// or failed — in which case the caller has only the structural answer in <paramref name="Suppresses"/>.
/// </param>
public sealed record PragmaVerifyResultDto(
    bool Suppresses,
    string FilePath,
    int Line,
    string DiagnosticId,
    int? DisableLine,
    int? RestoreLine,
    string Reason,
    bool? DiagnosticFiresAtLine = null);
