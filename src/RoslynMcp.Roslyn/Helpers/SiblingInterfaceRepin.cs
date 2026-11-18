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
        var candidates = new List<(Document Document, List<(TextSpan Span, string Text, INamedTypeSymbol Original)> Edits)>();
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
                var documentText = await document.GetTextAsync(ct).ConfigureAwait(false);
                var spelled = new List<(TextSpan Span, string Text, INamedTypeSymbol Original)>();
                foreach (var (node, symbol) in rebound)
                {
                    // `TryCreate` spells a non-generic, non-nested named type by namespace: anything else (a generic, nested, tuple,
                    // nullable, array, pointer or function-pointer alias target, a namespace alias) has no spelling it can
                    // produce, so refuse rather than leave the reference captured.
                    if (symbol is not INamedTypeSymbol { Arity: 0, ContainingType: null } original)
                    {
                        throw new PublicInvalidOperationException(
                            $"Generating interface '{name}' would rebind the reference to '{symbol!.ToDisplayString()}' ({Describe(symbol)}) on line " +
                            $"{documentText.Lines.GetLinePosition(node.SpanStart).Line + 1} of '{document.FilePath ?? document.Name}', " +
                            "which cannot be re-pinned: only a plain non-generic, non-nested named type can be spelled again; " +
                            "choose a different interface name.");
                    }

                    // A spelling that does not bind to the original at that position would silently mean another type (an extern
                    // alias hides the original while the global alias reaches a same-named type) or nothing: refuse, verified.
                    if (!GeneratedInterfaceReference.TryCreate(original, after, node.SpanStart, out var spelling))
                    {
                        throw new PublicInvalidOperationException(
                            $"Generating interface '{name}' would rebind the reference to '{original.ToDisplayString()}' on line " +
                            $"{documentText.Lines.GetLinePosition(node.SpanStart).Line + 1} of '{document.FilePath ?? document.Name}', " +
                            "which cannot be re-pinned: no spelling of that type reaches it from there (it is visible only through an " +
                            "extern alias, or another type of the same name is visible without one); choose a different interface name.");
                    }

                    spelled.Add((node.Span, spelling.ToString(), original));
                }

                candidates.Add((document, spelled));
            }

            if (project.AnalyzerReferences.Count > 0)
                await RefuseReboundGeneratedReferencesAsync(project, without.GetProject(project.Id)!, generated, name, ct).ConfigureAwait(false);
        }

        // One physical file linked into several projects is one edit: every copy gets the union of the spellings chosen for
        // it, so the copies never diverge and no copy is written last with a different binding. That is only sound when the
        // copies agree: copies with different text, a span spelled two ways, or a span that means a different type in another
        // copy refuse the preview rather than write one project's meaning into another's.
        var edits = new Dictionary<DocumentId, Dictionary<TextSpan, string>>();
        foreach (var (document, documentEdits) in candidates)
        {
            var (copies, divergent) = await LinkedCopiesAsync(marked, document, ct).ConfigureAwait(false);
            if (divergent.Count > 0)
            {
                throw new PublicInvalidOperationException(
                    $"Linked C# documents at '{document.FilePath}' have different content in different projects, so the preview cannot " +
                    $"re-pin the references to '{name}' in that one file consistently; reload the workspace, or unlink the file from one of the projects, and retry.");
            }

            var documentText = await document.GetTextAsync(ct).ConfigureAwait(false);
            foreach (var copy in copies)
            {
                // The source type's own document is edited again (its base list) after this step, in one copy only; the
                // linked-document reconciliation that follows on every route copies that final text to the others, so an edit
                // made here to a second copy would make the copies diverge there. Those other copies are still checked: the
                // reconciliation overwrites them with this spelling, so it must mean the same type in their project.
                var isOtherSourceCopy = copy.Id != sourceDocumentId && !string.IsNullOrWhiteSpace(sourceDocument.FilePath) &&
                    FileSystemPath.Comparer.Equals(copy.FilePath, sourceDocument.FilePath);
                Dictionary<TextSpan, string>? spans = null;
                if (!isOtherSourceCopy && !edits.TryGetValue(copy.Id, out spans)) edits[copy.Id] = spans = [];
                foreach (var (span, text, original) in documentEdits)
                {
                    if (copy.Id != document.Id && !await BindsToAsync(without, copy, span, original, ct).ConfigureAwait(false))
                        throw Disagreement(document, name, documentText.Lines.GetLinePosition(span.Start).Line + 1);
                    if (spans is null) continue;
                    if (spans.TryGetValue(span, out var existing))
                    {
                        if (existing != text) throw Disagreement(document, name, documentText.Lines.GetLinePosition(span.Start).Line + 1);
                        continue;
                    }

                    spans[span] = text;
                }
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

    /// <summary>
    /// The documents of <paramref name="solution"/> that are the same physical file as <paramref name="document"/> (linked copies):
    /// those with the same text (including <paramref name="document"/>), and those whose text differs.
    /// </summary>
    private static async Task<(List<Document> Same, List<Document> Divergent)> LinkedCopiesAsync(
        Solution solution, Document document, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath)) return ([document], []);
        var checksum = (await document.GetTextAsync(ct).ConfigureAwait(false)).GetChecksum();
        var same = new List<Document>();
        var divergent = new List<Document>();
        foreach (var candidate in solution.Projects.SelectMany(project => project.Documents))
        {
            if (!FileSystemPath.Comparer.Equals(candidate.FilePath, document.FilePath)) continue;
            if ((await candidate.GetTextAsync(ct).ConfigureAwait(false)).GetChecksum().SequenceEqual(checksum)) same.Add(candidate);
            else divergent.Add(candidate);
        }

        return (same, divergent);
    }

    /// <summary>Whether, in the baseline fork, the name at <paramref name="span"/> of <paramref name="copy"/> means <paramref name="original"/>.</summary>
    private static async Task<bool> BindsToAsync(Solution without, Document copy, TextSpan span, INamedTypeSymbol original, CancellationToken ct)
    {
        var baseline = without.GetDocument(copy.Id)
            ?? throw InvalidOperationErrors.Internal($"Document '{copy.Name}' is missing from the baseline fork.");
        var root = await baseline.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal($"Syntax for document '{copy.Name}' is unavailable.");
        var model = await baseline.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal($"Semantic model for document '{copy.Name}' is unavailable.");
        var node = root.DescendantNodes().OfType<IdentifierNameSyntax>().FirstOrDefault(candidate => candidate.Span == span);
        // A linked project compiles its own copy of the declaration, so the symbols differ by assembly: the same
        // fully-qualified type is the same meaning; a different type, or none, is a disagreement.
        return node is not null && model.GetSymbolInfo(node, ct).Symbol is INamedTypeSymbol bound &&
               bound.ToDisplayString() == original.ToDisplayString();
    }

    private static PublicInvalidOperationException Disagreement(Document document, string name, int line) =>
        new($"Linked C# documents at '{document.FilePath}' would pin the reference to '{name}' on line {line} to different types " +
            "in different projects, so the preview cannot write one spelling into every project; choose a different interface name.");

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

            var rebound = FindRebound(root, after, before, generated, name, ct);
            if (rebound.Count > 0)
            {
                var line = (await document.GetTextAsync(ct).ConfigureAwait(false)).Lines.GetLinePosition(rebound[0].Node.SpanStart).Line + 1;
                throw new PublicInvalidOperationException(
                    $"Generating interface '{name}' would rebind a reference to '{name}' in source-generated document '{document.HintName}' " +
                    $"(project '{project.Name}', line {line}), which no preview can edit; choose a different interface name.");
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

    private static string Describe(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => "a namespace",
        IArrayTypeSymbol => "an array type",
        IPointerTypeSymbol => "a pointer type",
        IFunctionPointerTypeSymbol => "a function pointer type",
        INamedTypeSymbol { IsTupleType: true } => "a tuple type",
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } => "a nullable value type",
        INamedTypeSymbol { Arity: > 0 } => "a generic type",
        INamedTypeSymbol { ContainingType: not null } => "a nested type",
        _ => "a type with no namespace spelling",
    };

    /// <summary>
    /// Identifiers spelled <paramref name="name"/> that bound to a type or namespace before the generated interface existed and
    /// do not bind to the same symbol now (a different type, or ambiguity). The earlier symbol need not be called
    /// <paramref name="name"/>: a using alias spelled that way reaches a differently named, generic, nested, tuple, nullable,
    /// array, pointer or function-pointer type, or a namespace, and the namespace member the new interface becomes beats the
    /// alias. With no <paramref name="before"/> model, names that now reach the generated interface.
    /// </summary>
    private static List<(IdentifierNameSyntax Node, ISymbol? Original)> FindRebound(
        SyntaxNode root, SemanticModel after, SemanticModel? before, INamedTypeSymbol generated, string name, CancellationToken ct)
    {
        var rebound = new List<(IdentifierNameSyntax, ISymbol?)>();
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

            var earlier = before.GetSymbolInfo(node, ct).Symbol;
            // `before` and `after` come from two compilations, so symbols are compared by identity across compilations
            // (a named type) or by display string (a namespace, an array, a pointer, a function pointer): never by instance.
            var unchanged = earlier switch
            {
                INamedTypeSymbol type => GeneratedInterfaceReference.IsSame(current.Symbol, type),
                INamespaceSymbol ns => current.Symbol is INamespaceSymbol now && now.ToDisplayString() == ns.ToDisplayString(),
                ITypeSymbol other => current.Symbol is ITypeSymbol now && now.ToDisplayString() == other.ToDisplayString(),
                _ => true,
            };
            if (!unchanged) rebound.Add((node, earlier));
        }

        return rebound;
    }
}
