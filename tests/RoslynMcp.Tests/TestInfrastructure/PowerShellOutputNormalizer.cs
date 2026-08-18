using System.Text.RegularExpressions;

namespace RoslynMcp.Tests;

/// <summary>
/// Normalizes captured PowerShell console output so substring assertions survive the
/// host's error formatter.
/// </summary>
/// <remarks>
/// <para>
/// Write-Error renders through PowerShell's formatter, which hard-wraps the message at the
/// console width and prefixes every continuation line with a '|' gutter. The wrap column
/// depends on the host, so the same message splits differently on a Windows self-hosted
/// runner than on a GitHub-hosted Linux runner. A message reading "require a major bump" on
/// one host arrives as "require a" + newline + " | major bump" on the other, and a naive
/// substring assertion fails against output that is in fact correct.
/// </para>
/// <para>
/// Normalizing strips ANSI styling, rejoins gutter-wrapped continuation lines, and collapses
/// whitespace runs, yielding host-independent single-line text to assert against.
/// </para>
/// </remarks>
internal static class PowerShellOutputNormalizer
{
    private const char AnsiEscape = (char)0x1B;

    private static readonly Regex AnsiStylePattern = new(
        AnsiEscape + @"\[[0-9;]*m",
        RegexOptions.Compiled);

    // PowerShell's wrapped-line gutter: newline, optional indent, '|', optional spacing.
    // Anchoring on the newline keeps legitimate mid-line pipes intact.
    private static readonly Regex ContinuationGutterPattern = new(
        @"\r?\n[ \t]*\|[ \t]*",
        RegexOptions.Compiled);

    private static readonly Regex WhitespaceRunPattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="output"/> with ANSI styling removed, formatter line-wrapping
    /// rejoined, and whitespace collapsed to single spaces.
    /// </summary>
    public static string Normalize(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var withoutAnsi = AnsiStylePattern.Replace(output, string.Empty);
        var rejoined = ContinuationGutterPattern.Replace(withoutAnsi, " ");
        return WhitespaceRunPattern.Replace(rejoined, " ").Trim();
    }
}
