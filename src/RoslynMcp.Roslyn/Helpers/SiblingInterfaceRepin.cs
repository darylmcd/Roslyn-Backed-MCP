using Microsoft.CodeAnalysis;
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

        // Same texts, same spans: only the generated document differs, so no span ever crosses between two solutions.
        var without = marked.RemoveDocument(interfaceDocumentId);
        var result = marked;
        var repinned = false;
        var name = generated.Name;
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
                var originals = rebound.ToDictionary(item => item.Node, item => item.Original!);
                var updated = root.ReplaceNodes(
                    originals.Keys,
                    (original, _) => GeneratedInterfaceReference.Create(originals[original], after, original.SpanStart).WithTriviaFrom(original));
                result = result.WithDocumentSyntaxRoot(document.Id, updated);
                repinned = true;
            }

            if (project.AnalyzerReferences.Count > 0)
                await RefuseReboundGeneratedReferencesAsync(project, without.GetProject(project.Id)!, generated, name, ct).ConfigureAwait(false);
        }

        return new Result(result, typeMark, repinned);
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
        (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString().Contains(name, StringComparison.Ordinal);

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
