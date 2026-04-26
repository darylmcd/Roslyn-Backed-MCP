using System.Globalization;
using System.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Formatters;

/// <summary>
/// response-format-json-markdown-parameter (P4 UX): renders a
/// <see cref="WorkspaceValidationDto"/> as a compact human-readable markdown summary.
/// Used by <c>validate_workspace</c> when the caller passes
/// <c>responseFormat=markdown</c>; the default JSON wire shape is preserved when the
/// parameter is omitted or set to <c>json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Output target is roughly 30 lines vs the typical 120-line JSON envelope on
/// medium-sized solutions. The formatter is single-pass O(N) over the diagnostics
/// and discovered-tests lists; per-item rows are capped (configurable via
/// <see cref="MaxDiagnosticRows"/> / <see cref="MaxTestRows"/>) so a runaway
/// response stays bounded even when the underlying DTO does not (e.g. when
/// <c>summary=false</c> on a large solution with many errors).
/// </para>
/// <para>
/// Verdict-equivalence contract: the markdown shape MUST surface the same
/// <c>OverallStatus</c> verdict as the JSON shape — both modes are produced from
/// the same <see cref="WorkspaceValidationDto"/> instance, so a caller that wires
/// the markdown table into a downstream gate gets the same pass/fail decision as
/// a caller that parses the JSON envelope.
/// </para>
/// </remarks>
internal static class ValidateWorkspaceMarkdownFormatter
{
    /// <summary>Cap on per-diagnostic rows in the markdown output.</summary>
    public const int MaxDiagnosticRows = 20;

    /// <summary>Cap on per-test rows in the markdown output.</summary>
    public const int MaxTestRows = 20;

    /// <summary>
    /// Render <paramref name="dto"/> as a compact markdown summary. The returned
    /// string is the entire tool response (no JSON envelope) and is intended to
    /// flow straight through the MCP transport.
    /// </summary>
    public static string Format(WorkspaceValidationDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var sb = new StringBuilder(1024);

        sb.Append("# validate_workspace — ").AppendLine(dto.OverallStatus);
        sb.AppendLine();

        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        AppendRow(sb, "Overall status", dto.OverallStatus);
        AppendRow(sb, "Compile errors", dto.CompileResult.ErrorCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Compile warnings", dto.CompileResult.WarningCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Analyzer/compile error diagnostics", dto.ErrorDiagnostics.Count.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Warning diagnostics", dto.WarningCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Changed file paths (resolved)", dto.ChangedFilePaths.Count.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Unknown file paths", dto.UnknownFilePaths.Count.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Discovered tests", dto.DiscoveredTests.Count.ToString(CultureInfo.InvariantCulture));
        if (dto.TestRunResult is { } run)
        {
            AppendRow(sb, "Tests total", run.Total.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "Tests passed", run.Passed.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "Tests failed", run.Failed.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "Tests skipped", run.Skipped.ToString(CultureInfo.InvariantCulture));
        }

        if (dto.ErrorDiagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Error diagnostics");
            sb.AppendLine();
            sb.AppendLine("| Id | File | Line | Message |");
            sb.AppendLine("|----|------|------|---------|");
            var shown = 0;
            foreach (var diag in dto.ErrorDiagnostics)
            {
                if (shown >= MaxDiagnosticRows) break;
                var lineText = diag.StartLine?.ToString(CultureInfo.InvariantCulture) ?? "";
                sb.Append("| ").Append(EscapeCell(diag.Id))
                  .Append(" | ").Append(EscapeCell(diag.FilePath ?? ""))
                  .Append(" | ").Append(lineText)
                  .Append(" | ").Append(EscapeCell(diag.Message))
                  .AppendLine(" |");
                shown++;
            }
            if (dto.ErrorDiagnostics.Count > MaxDiagnosticRows)
            {
                sb.Append("| … | (").Append((dto.ErrorDiagnostics.Count - MaxDiagnosticRows).ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" more truncated) | | |");
            }
        }

        if (dto.DiscoveredTests.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Discovered tests");
            sb.AppendLine();
            sb.AppendLine("| Test | Project |");
            sb.AppendLine("|------|---------|");
            var shown = 0;
            foreach (var test in dto.DiscoveredTests)
            {
                if (shown >= MaxTestRows) break;
                sb.Append("| ").Append(EscapeCell(test.DisplayName))
                  .Append(" | ").Append(EscapeCell(test.ProjectName))
                  .AppendLine(" |");
                shown++;
            }
            if (dto.DiscoveredTests.Count > MaxTestRows)
            {
                sb.Append("| (").Append((dto.DiscoveredTests.Count - MaxTestRows).ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" more truncated) | |");
            }
        }

        if (dto.TestRunResult is { Failures.Count: > 0 } runResult)
        {
            sb.AppendLine();
            sb.AppendLine("## Test failures");
            sb.AppendLine();
            sb.AppendLine("| Test | Message |");
            sb.AppendLine("|------|---------|");
            var shown = 0;
            foreach (var failure in runResult.Failures)
            {
                if (shown >= MaxTestRows) break;
                sb.Append("| ").Append(EscapeCell(failure.DisplayName))
                  .Append(" | ").Append(EscapeCell(failure.Message))
                  .AppendLine(" |");
                shown++;
            }
            if (runResult.Failures.Count > MaxTestRows)
            {
                sb.Append("| (").Append((runResult.Failures.Count - MaxTestRows).ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" more truncated) | |");
            }
        }

        if (dto.DotnetTestFilter is { Length: > 0 } filter)
        {
            sb.AppendLine();
            sb.Append("**Re-run filter:** `dotnet test --filter ").Append(filter).AppendLine("`");
        }

        if (dto.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (var warning in dto.Warnings)
            {
                sb.Append("- ").AppendLine(warning);
            }
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string metric, string value)
    {
        sb.Append("| ").Append(metric).Append(" | ").Append(value).AppendLine(" |");
    }

    /// <summary>
    /// Escape a cell value for safe inclusion in a markdown table row: collapse
    /// newlines (which would terminate the row) and escape pipe characters
    /// (which would split the row into extra columns).
    /// </summary>
    private static string EscapeCell(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Collapse CR/LF to spaces; replace pipe with backslash-pipe so the column
        // boundary is preserved while still rendering the original character.
        var collapsed = value.Replace("\r\n", " ", StringComparison.Ordinal)
                             .Replace('\n', ' ')
                             .Replace('\r', ' ');
        return collapsed.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
