using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// A generated interface can capture names: any bare <c>IFoo</c> in its namespace (or a nested one) now binds to the new
/// <c>IFoo</c>, whether it meant the implemented sibling, an unrelated type of the same name, or sits in a project that would
/// now see two. This pins every such reference to what it bound to before the interface existed, so the preview keeps the
/// meaning of code it did not set out to change. The rebound set is the difference between two bindings of the same text: the
/// fork with the generated document and the same fork without it.
/// </summary>
internal static class SiblingInterfaceRepin
{
    /// <param name="Solution">The fork with every captured reference repaired.</param>
    /// <param name="TypeMark">Locates the source type's declaration after the repairs moved its text.</param>
    /// <param name="Repinned">Whether any reference was rewritten.</param>
    public sealed record Result(Solution Solution, SyntaxAnnotation TypeMark, bool Repinned);

    /// <summary>
    /// <paramref name="typeSpan"/> is the source type's span in <paramref name="forked"/>; the fork has not edited that document yet.
    /// A captured reference in a document no preview can edit (source-generated) refuses the preview: it would otherwise
    /// silently change meaning even when it still compiles.
    /// </summary>
    public static async Task<Result> RepinAsync(
        Solution forked, DocumentId interfaceDocumentId, DocumentId sourceDocumentId, TextSpan typeSpan, CancellationToken ct)
    {
        var generated = await GeneratedInterfaceReference.ResolveGeneratedAsync(forked, interfaceDocumentId, ct).ConfigureAwait(false);
        var sourceDocument = forked.GetDocument(sourceDocumentId)
            ?? throw InvalidOperationErrors.Internal("Source document is unavailable.");
        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Source syntax is unavailable.");
        var typeMark = new SyntaxAnnotation();
        var declaration = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(node => node.Span == typeSpan);
        var marked = forked.WithDocumentSyntaxRoot(
            sourceDocumentId, sourceRoot.ReplaceNode(declaration, declaration.WithAdditionalAnnotations(typeMark)));

        // The baseline is this same fork minus the generated document: any other change the fork made is part of the world both
        // bindings share, so only the generated document can move a binding. Same texts, same spans: no span ever crosses solutions.
        var without = marked.RemoveDocument(interfaceDocumentId);
        var name = generated.Name;
        var candidates = new List<(Document Document, List<(TextSpan Span, string Text)> Edits)>();
        foreach (var project in marked.Projects)
        {
            foreach (var document in project.Documents.Where(candidate => candidate.Id != interfaceDocumentId))
            {
                if (!await MentionsAsync(document, name, ct).ConfigureAwait(false)) continue;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                    ?? throw InvalidOperationErrors.Internal($"Syntax for document '{document.Name}' is unavailable.");
                var after = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
                    ?? throw InvalidOperationErrors.Internal($"Semantic model for document '{document.Name}' is unavailable.");
                var before = await (without.GetDocument(document.Id)
                    ?? throw InvalidOperationErrors.Internal($"Document '{document.Name}' is missing from the baseline fork."))
                    .GetSemanticModelAsync(ct).ConfigureAwait(false)
                    ?? throw InvalidOperationErrors.Internal($"Baseline semantic model for document '{document.Name}' is unavailable.");

                var rebound = FindRebound(root, after, before, generated, name, ct);
                if (rebound.Count == 0) continue;
                candidates.Add((document, rebound
                    .Select(item => (item.Node.Span, GeneratedInterfaceReference.Create(item.Original!, after, item.Node.SpanStart).ToString()))
                    .ToList()));
            }

            if (project.AnalyzerReferences.Count > 0)
                await RefuseReboundGeneratedReferencesAsync(project, without.GetProject(project.Id)!, generated, name, ct).ConfigureAwait(false);
        }

        // One physical file linked into several projects is one edit: every copy with the same text gets the union of the
        // spellings chosen for it (solution order wins a span two copies spell differently), so the copies never diverge and
        // no copy is written last with a different binding. The compile gate then proves each project accepts that spelling.
        var edits = new Dictionary<DocumentId, Dictionary<TextSpan, string>>();
        foreach (var (document, documentEdits) in candidates)
        {
            foreach (var copy in await LinkedCopiesAsync(marked, document, ct).ConfigureAwait(false))
            {
                // The source type's own document is edited again (its base list) after this step, in one copy only; a
                // route that reconciles linked copies (cross-project) copies that final text to the others, so an edit
                // made here to a second copy would make the copies diverge there.
                if (copy.Id != sourceDocumentId && !string.IsNullOrWhiteSpace(sourceDocument.FilePath) &&
                    FileSystemPath.Comparer.Equals(copy.FilePath, sourceDocument.FilePath)) continue;
                if (!edits.TryGetValue(copy.Id, out var spans)) edits[copy.Id] = spans = [];
                foreach (var (span, text) in documentEdits) spans.TryAdd(span, text);
            }
        }

        var result = marked;
        foreach (var (documentId, spans) in edits)
        {
            var root = await marked.GetDocument(documentId)!.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal("Syntax for a linked document is unavailable.");
            var updated = root.ReplaceNodes(
                root.DescendantNodes().OfType<IdentifierNameSyntax>().Where(node => spans.ContainsKey(node.Span)),
                (original, _) => SyntaxFactory.ParseTypeName(spans[original.Span]).WithTriviaFrom(original));
            result = result.WithDocumentSyntaxRoot(documentId, updated);
        }

        return new Result(result, typeMark, edits.Count > 0);
    }

    /// <summary>The documents of <paramref name="solution"/> that are the same physical file with the same text (linked copies), including <paramref name="document"/>.</summary>
    private static async Task<List<Document>> LinkedCopiesAsync(Solution solution, Document document, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath)) return [document];
        var checksum = (await document.GetTextAsync(ct).ConfigureAwait(false)).GetChecksum();
        var copies = new List<Document>();
        foreach (var candidate in solution.Projects.SelectMany(project => project.Documents))
        {
            if (!FileSystemPath.Comparer.Equals(candidate.FilePath, document.FilePath)) continue;
            if ((await candidate.GetTextAsync(ct).ConfigureAwait(false)).GetChecksum().SequenceEqual(checksum)) copies.Add(candidate);
        }

        return copies;
    }

    private static async Task RefuseReboundGeneratedReferencesAsync(
        Project project, Project without, INamedTypeSymbol generated, string name, CancellationToken ct)
    {
        var previous = (await without.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false)).ToList();
        foreach (var document in await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false))
        {
            if (!await MentionsAsync(document, name, ct).ConfigureAwait(false)) continue;
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Syntax for generated document '{document.Name}' is unavailable.");
            var after = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Semantic model for generated document '{document.Name}' is unavailable.");

            // A generator whose output changed with the new type has no baseline to diff: then any use of the name that
            // now reaches the generated interface, or is ambiguous with it, counts as captured.
            SemanticModel? before = null;
            var earlier = previous.FirstOrDefault(candidate => candidate.HintName == document.HintName);
            if (earlier is not null && (await earlier.GetTextAsync(ct).ConfigureAwait(false)).ContentEquals(await document.GetTextAsync(ct).ConfigureAwait(false)))
                before = await earlier.GetSemanticModelAsync(ct).ConfigureAwait(false);

            if (FindRebound(root, after, before, generated, name, ct).Count > 0)
            {
                throw new PublicInvalidOperationException(
                    $"Generating interface '{name}' would rebind a reference to '{name}' in source-generated document '{document.HintName}' " +
                    $"(project '{project.Name}'), which no preview can edit; choose a different interface name.");
            }
        }
    }

    private static async Task<bool> MentionsAsync(Document document, string name, CancellationToken ct) =>
        Mentions(await document.GetTextAsync(ct).ConfigureAwait(false), name, 8192);

    /// <summary>Whether <paramref name="text"/> contains <paramref name="name"/>, read in <paramref name="chunk"/>-sized windows (a name may straddle two) so no document is copied to one string.</summary>
    internal static bool Mentions(SourceText text, string name, int chunk)
    {
        var buffer = new char[Math.Max(chunk, name.Length * 2)];
        var overlap = name.Length - 1;
        for (var start = 0; start < text.Length;)
        {
            var count = Math.Min(buffer.Length, text.Length - start);
            text.CopyTo(start, buffer, 0, count);
            if (buffer.AsSpan(0, count).IndexOf(name, StringComparison.Ordinal) >= 0) return true;
            if (start + count >= text.Length) return false;
            start += count - overlap;
        }

        return false;
    }

    /// <summary>
    /// Names that bound to a type called <paramref name="name"/> before the generated interface existed and do not bind to it
    /// now (a different type, or ambiguity). With no <paramref name="before"/> model, names that now reach the generated interface.
    /// </summary>
    private static List<(IdentifierNameSyntax Node, INamedTypeSymbol? Original)> FindRebound(
        SyntaxNode root, SemanticModel after, SemanticModel? before, INamedTypeSymbol generated, string name, CancellationToken ct)
    {
        var rebound = new List<(IdentifierNameSyntax, INamedTypeSymbol?)>();
        foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!string.Equals(node.Identifier.ValueText, name, StringComparison.Ordinal)) continue;
            var current = after.GetSymbolInfo(node, ct);
            if (before is null)
            {
                if (GeneratedInterfaceReference.IsSame(current.Symbol, generated) ||
                    (current.Symbol is null && current.CandidateSymbols.Any(candidate => GeneratedInterfaceReference.IsSame(candidate, generated))))
                {
                    rebound.Add((node, null));
                }

                continue;
            }

            if (before.GetSymbolInfo(node, ct).Symbol is not INamedTypeSymbol { Arity: 0 } original ||
                !string.Equals(original.Name, name, StringComparison.Ordinal) ||
                GeneratedInterfaceReference.IsSame(current.Symbol, original))
            {
                continue;
            }

            rebound.Add((node, original));
        }

        return rebound;
    }
}
