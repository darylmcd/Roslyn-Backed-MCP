using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// apply-with-verify-diff-not-counts: shared helper that builds an "identity" set for
/// Error-severity <see cref="DiagnosticDto"/> entries so verify steps in <c>apply_with_verify</c>
/// and <c>apply_text_edit/apply_multi_file_edit verify=true</c> can subtract pre-existing
/// errors from the introduced set without false-positive rollback.
/// </summary>
/// <remarks>
/// <para>
/// Identity format: <c>id|file|line</c>. The previous fingerprint format
/// (<c>id|file:line:col|message</c>) included column AND message text — both of which can
/// flip across the pre-vs-post compile pair without the apply being to blame:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Message text</b> can change for a pre-existing diagnostic when the surrounding code
/// shifts (e.g. a <c>CS0103</c> "name 'X' does not exist" diagnostic whose nearby identifier
/// gets renamed by an unrelated apply, causing the analyzer's message to widen). Including
/// the message in the fingerprint causes the post-apply diagnostic to look new even though
/// the underlying error has not moved.
/// </description></item>
/// <item><description>
/// <b>Column</b> can shift for a pre-existing diagnostic when an edit on the same line moves
/// the trailing tokens. Line is stable in practice; column is fragile.
/// </description></item>
/// </list>
/// <para>
/// Switching the identity to <c>id|file|line</c> matches the row's stated trigger condition:
/// rollback ONLY when the post-apply diagnostic set has identities (id+file+line tuples)
/// that did not appear in the pre-apply set. Pre-existing diagnostics whose severity flipped,
/// whose message changed, or whose column shifted no longer trigger false-positive rollback.
/// </para>
/// <para>
/// Retro evidence: ~14% of <c>apply_with_verify</c> rollbacks (5/36 over 14 days, per
/// <c>apply-with-verify-diff-not-counts</c> backlog row) are false positives where a
/// pre-existing diagnostic flipped severity class on the post-apply build path even though
/// the apply itself was innocent.
/// </para>
/// </remarks>
public static class DiagnosticIdentitySet
{
    /// <summary>
    /// Formats the stable identity key for a single diagnostic. Format: <c>id|file|line</c>.
    /// Case-sensitive ordinal comparison via <see cref="StringComparer.Ordinal"/> is
    /// authoritative on Windows / Linux file systems; callers that need case-insensitive
    /// path matching should normalize <see cref="DiagnosticDto.FilePath"/> before passing
    /// the diagnostic in.
    /// </summary>
    public static string FormatIdentity(DiagnosticDto diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return $"{diagnostic.Id}|{diagnostic.FilePath}|{diagnostic.StartLine}";
    }

    /// <summary>
    /// Extracts the <see cref="HashSet{String}"/> of Error-severity identity keys from a
    /// compile-check result. Non-error severities are skipped because the verify step only
    /// rolls back on introduced ERRORS, not on warnings or info.
    /// </summary>
    public static HashSet<string> ExtractErrorIdentities(CompileCheckDto check)
    {
        ArgumentNullException.ThrowIfNull(check);

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in check.Diagnostics)
        {
            if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            identities.Add(FormatIdentity(d));
        }

        return identities;
    }

    /// <summary>
    /// Extracts the <see cref="HashSet{String}"/> of Error-severity identity keys from an
    /// arbitrary diagnostic enumerable (e.g. <c>baseline.Diagnostics</c> from a
    /// <c>compile_check</c>) — supports the EditService path where the baseline already
    /// arrives as a flat <see cref="IReadOnlyList{DiagnosticDto}"/>.
    /// </summary>
    public static HashSet<string> ExtractErrorIdentities(IEnumerable<DiagnosticDto> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in diagnostics)
        {
            if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            identities.Add(FormatIdentity(d));
        }

        return identities;
    }
}
