using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Checks 1-based coordinates before adding a column to a source-line offset.</summary>
internal static class SourcePosition
{
    public static void ValidatePositiveCoordinates(int line, int column, string lineParameter, string columnParameter)
    {
        ValidateBounds(line, null, lineParameter, string.Empty);
        ValidateBounds(column, null, columnParameter, string.Empty);
    }

    public static int StrictCaret(SourceText text, int line, int column,
        string lineParameter = "line", string columnParameter = "column")
    {
        var sourceLine = GetLine(text, line, lineParameter);
        return Resolve(sourceLine, line, column, (long)sourceLine.Span.Length + 1, columnParameter);
    }

    public static int ProbeTrivia(SourceText text, int line, int column)
    {
        var sourceLine = GetLine(text, line, "line");
        // A line break belongs to this line; its exclusive end belongs to the next line.
        // Only the final line can also accept the caret at EOF.
        var maxColumn = sourceLine.EndIncludingLineBreak > sourceLine.End
            ? (long)sourceLine.SpanIncludingLineBreak.Length
            : (long)sourceLine.Span.Length + 1;
        return Resolve(sourceLine, line, column, maxColumn, "column");
    }

    public static int ClampedCodeActionCaret(SourceText text, int line, int column)
    {
        var sourceLine = GetLine(text, line, "startLine");
        var maxColumn = (long)sourceLine.Span.Length + 1;
        // Clamp before adding so even int.MaxValue cannot overflow or advance to another line.
        var clampedColumn = Math.Min((long)column, maxColumn);
        ValidateBounds(clampedColumn, maxColumn, "startColumn", $" for line {line}");
        return checked(sourceLine.Start + ((int)clampedColumn - 1));
    }

    private static TextLine GetLine(SourceText text, int line, string parameter)
    {
        ValidateBounds(line, text.Lines.Count, parameter, string.Empty);
        return text.Lines[line - 1];
    }

    private static int Resolve(TextLine sourceLine, int line, int column, long maxColumn, string parameter)
    {
        ValidateBounds(column, maxColumn, parameter, $" for line {line}");
        return checked(sourceLine.Start + (column - 1));
    }

    private static void ValidateBounds(long value, long? upperBound, string parameter, string context)
    {
        if (value >= 1 && (!upperBound.HasValue || value <= upperBound.Value))
            return;

        var correction = upperBound.HasValue
            ? $"valid range: 1..{upperBound.Value}"
            : $"{parameter} must be >= 1 (1-based)";
        throw new PublicArgumentException($"{parameter} {value} is out of range{context} ({correction}).", parameter);
    }
}
