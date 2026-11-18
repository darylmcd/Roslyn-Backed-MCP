using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Resolves the document a find-references location sits in to the editable document of the solution being rewritten.
/// A reference sweep also reports locations in source-generated documents, which <see cref="Solution.GetDocument(DocumentId)"/>
/// does not return and which no preview can edit.
/// </summary>
internal static class ReferenceDocumentResolver
{
    /// <summary>
    /// The editable document, or <see langword="null"/> when <paramref name="located"/> is source-generated (the generator
    /// owns its text and re-emits it, so a preview leaves it untouched). A non-generated document that the solution no longer
    /// holds is a real inconsistency and throws.
    /// </summary>
    public static Document? ResolveEditable(Solution solution, Document located)
    {
        if (located is SourceGeneratedDocument) return null;
        return solution.GetDocument(located.Id)
            ?? throw InvalidOperationErrors.Internal($"Reference document '{located.Name}' is not part of the solution being rewritten.");
    }
}
