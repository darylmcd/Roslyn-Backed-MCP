using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// The wire shape returned by <c>get_source_text</c>. Property names are PascalCase because
/// <see cref="JsonDefaults.Indented"/> applies <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/>,
/// which emits the same camelCase keys the tool has always produced.
/// </summary>
internal sealed record SourceTextProjection(
    string FilePath,
    int TotalLineCount,
    int RequestedStartLine,
    int RequestedEndLine,
    int ReturnedStartLine,
    int ReturnedEndLine,
    bool Truncated,
    string Text);

/// <summary>
/// Request validation and wire projection for <c>get_source_text</c>, kept out of the tool
/// endpoint so <c>WorkspaceTools.GetSourceText</c> stays pure orchestration. Line slicing and
/// line counting stay delegated to <see cref="SourceTextSlicer"/>, which the
/// <c>roslyn://workspace/{id}/file/.../lines/{N-M}</c> resource surface shares.
/// </summary>
internal static class SourceTextRequestProjection
{
    /// <summary>
    /// Validates the caller-supplied bounds that can be checked before the document is read.
    /// Throws <see cref="ArgumentException"/> with the historically stable paramNames.
    /// </summary>
    public static void ValidateRequest(int maxChars, int? startLine, int? endLine)
    {
        if (maxChars <= 0)
            throw new ArgumentException($"maxChars must be greater than 0 (got {maxChars}).", nameof(maxChars));
        if (startLine is < 1)
            throw new ArgumentException($"startLine must be >= 1 (got {startLine.Value}).", nameof(startLine));
        if (endLine is < 1)
            throw new ArgumentException($"endLine must be >= 1 (got {endLine.Value}).", nameof(endLine));
        if (startLine.HasValue && endLine.HasValue && startLine.Value > endLine.Value)
            throw new ArgumentException(
                $"startLine ({startLine.Value}) must be <= endLine ({endLine.Value}).",
                nameof(startLine));
    }

    /// <summary>
    /// Counts lines, rejects a start line past EOF, clamps the end line to the file end,
    /// slices, and applies the character cap with the truncation marker.
    /// </summary>
    public static SourceTextProjection Project(string filePath, string text, int? startLine, int? endLine, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(text);

        var totalLineCount = SourceTextSlicer.CountLines(text);
        var requestedStart = startLine ?? 1;
        var requestedEnd = endLine ?? totalLineCount;

        if (requestedStart > totalLineCount)
            throw new ArgumentException(
                $"startLine ({requestedStart}) is past the end of the file ({totalLineCount} lines).",
                nameof(startLine));

        // Clamp endLine to the file end so callers asking for "lines 100..1000" on a
        // 200-line file get lines 100..200 instead of an error.
        var returnedEnd = Math.Min(requestedEnd, totalLineCount);
        var returnedStart = requestedStart;

        var slice = SourceTextSlicer.SliceLines(text, returnedStart, returnedEnd);

        var truncated = false;
        if (slice.Length > maxChars)
        {
            slice = slice.Substring(0, maxChars) + $"\n[TRUNCATED at {maxChars} characters — re-request a narrower line range to see the rest]";
            truncated = true;
        }

        return new SourceTextProjection(
            filePath,
            totalLineCount,
            requestedStart,
            requestedEnd,
            returnedStart,
            returnedEnd,
            truncated,
            slice);
    }
}
