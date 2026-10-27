using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Formats only syntax carrying <see cref="Formatter.Annotation"/>, in the file's own line ending.</summary>
internal static class AnnotatedFormatting
{
    /// <summary>
    /// Formats the annotated nodes of <paramref name="edited"/>. Synthesized nodes take the line ending of
    /// <paramref name="originalText"/>; otherwise a file without an <c>end_of_line</c> editorconfig entry gains
    /// lines in the formatter's platform default.
    /// </summary>
    internal static async Task<Document> FormatAsync(Document edited, string originalText, CancellationToken ct)
    {
        var lineEnding = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var options = (await edited.GetOptionsAsync(ct).ConfigureAwait(false))
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, lineEnding);
        return await Formatter.FormatAsync(edited, Formatter.Annotation, options, ct).ConfigureAwait(false);
    }
}
