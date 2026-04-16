using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Item #4 — shared helper for running Roslyn's <see cref="Formatter"/> on a document.
/// Services that synthesize <see cref="SyntaxNode"/>s with <see cref="Microsoft.CodeAnalysis.CSharp.SyntaxFactory"/>
/// previously called <c>.NormalizeWhitespace()</c> on the result and then serialized
/// via <c>.ToFullString()</c>. <c>NormalizeWhitespace</c> strips trivia from nodes
/// that were built without elastic trivia, producing shapes like <c>publicinterfaceI…</c>
/// (FORMAT-BUG-001 family — cross-project interface extraction, dependency_inversion_preview,
/// change_signature_preview op=add, split_class_preview, extract_method_preview).
///
/// Using <see cref="Formatter.FormatAsync(Document, CancellationToken)"/> on a <see cref="Document"/>
/// routes the nodes through Roslyn's real formatter, which understands C# grammar and
/// produces human-readable output. This helper centralizes the pattern so every synthesizer
/// gets the same behavior.
/// </summary>
internal static class SyntaxFormatter
{
    /// <summary>
    /// Formats a <see cref="Document"/> with Roslyn's <see cref="Formatter"/> and returns
    /// the updated <see cref="Solution"/>. Pass the document that was just mutated with a
    /// synthesized node; the formatter rewrites the whole document which keeps existing
    /// code untouched (it is re-parseable as a no-op) while rendering the new content with
    /// proper spacing.
    /// </summary>
    public static async Task<Solution> FormatDocumentAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var formatted = await Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        return formatted.Project.Solution;
    }

    /// <summary>
    /// Replaces a document's root with <paramref name="newRoot"/> and runs the formatter.
    /// Use when the caller has a synthesized root ready to drop into a document on a
    /// detached solution — the document is reprojected onto the solution and formatted
    /// in one step.
    /// </summary>
    public static async Task<Solution> ReplaceAndFormatAsync(
        Solution solution,
        DocumentId documentId,
        SyntaxNode newRoot,
        CancellationToken cancellationToken)
    {
        var withNewRoot = solution.WithDocumentSyntaxRoot(documentId, newRoot);
        var document = withNewRoot.GetDocument(documentId)
            ?? throw new InvalidOperationException($"Document '{documentId}' missing after WithDocumentSyntaxRoot.");
        return await FormatDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }
}
