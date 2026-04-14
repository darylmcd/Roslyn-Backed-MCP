namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Extracts a 1-based inclusive line range from a string source. Shared by
/// <c>get_source_text</c> (tool) and <c>roslyn://workspace/{id}/file/.../lines/{N-M}</c>
/// (resource) so both surfaces honour the same slicing semantics.
/// </summary>
public static class SourceTextSlicer
{
    /// <summary>
    /// Returns the inclusive substring of <paramref name="text"/> covering lines
    /// <paramref name="startLine"/>..<paramref name="endLine"/> (1-based). Includes the
    /// trailing line break of the last line so concatenated slices reassemble cleanly.
    /// Returns <see cref="string.Empty"/> when <paramref name="startLine"/> is past the end
    /// of the text.
    /// </summary>
    public static string SliceLines(string text, int startLine, int endLine)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Scan once; cheaper than allocating a string-array via Split when the slice is small.
        var startCharIndex = 0;
        var currentLine = 1;
        for (var i = 0; i < text.Length && currentLine < startLine; i++)
        {
            if (text[i] == '\n')
            {
                currentLine++;
                startCharIndex = i + 1;
            }
        }
        if (currentLine < startLine) return string.Empty;

        var endCharIndex = text.Length;
        var lineCounter = currentLine;
        for (var i = startCharIndex; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (lineCounter == endLine)
                {
                    endCharIndex = i + 1;
                    break;
                }
                lineCounter++;
            }
        }

        return text.Substring(startCharIndex, endCharIndex - startCharIndex);
    }

    /// <summary>
    /// Counts lines in <paramref name="text"/>. Empty text counts as 1 line.
    /// </summary>
    public static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        // Last line that ends with \n shouldn't be double-counted.
        if (text[^1] == '\n') count--;
        return count;
    }
}
