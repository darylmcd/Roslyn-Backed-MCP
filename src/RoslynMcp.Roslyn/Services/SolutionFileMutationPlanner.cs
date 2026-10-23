using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Renders the physical effects of a snapshot delta without publishing any writes.</summary>
internal static class SolutionFileMutationPlanner
{
    internal sealed record Mutation(string Path, byte[]? Before, byte[]? After, string OldText, string NewText);

    internal static async Task AssertCurrentDiskAuthorityAsync(Solution current, CompilerReferenceAuthority references, CancellationToken ct)
    {
        foreach (var project in current.Projects)
        {
            references.AssertCurrent(project);
            foreach (var document in SolutionFileMutationPlanner.AllDocuments(project))
            {
                if (document.FilePath is not { } path) continue;
                WorkspaceManager.EnsurePhysicalPath(path);
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                using var input = PhysicalInputReader.OpenRead(path, ct);
                using var reader = new StreamReader(input.Stream, text.Encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                if (!string.Equals(text.ToString(), await reader.ReadToEndAsync(ct).ConfigureAwait(false), StringComparison.Ordinal))
                    throw new PublicInvalidOperationException("The source documents have external edits; reload the workspace and recreate the preview.");
            }
        }

    }

    internal static string DocumentKind(TextDocument document) => document is Document ? "Compile"
        : document.Project.GetAdditionalDocument(document.Id) is not null ? "AdditionalFiles" : "EditorConfigFiles";

    internal static void ValidateDelta(Solution original, Solution modified)
    {
        foreach (var change in modified.GetChanges(original).GetProjectChanges())
        {
            var before = change.OldProject;
            var after = change.NewProject;
            if (!FileSystemPath.Comparer.Equals(before.OutputFilePath, after.OutputFilePath)
                && !DocumentChanges(original, modified).Any(delta => delta.Old?.FilePath is { } oldPath
                    && FileSystemPath.Comparer.Equals(oldPath, before.FilePath)
                    && FileSystemPath.Comparer.Equals(delta.New?.FilePath, after.FilePath)))
                throw new PublicInvalidOperationException("A project output change requires its authored project-file text in the reviewed delta.");
            if (!CompilationOptionsMatchPhysicalDelta(change) || !Equals(before.ParseOptions, after.ParseOptions)
                || before.Name != after.Name || before.AssemblyName != after.AssemblyName || before.Language != after.Language
                || !FileSystemPath.Comparer.Equals(before.FilePath, after.FilePath)
                || change.GetAddedMetadataReferences().Any() || change.GetRemovedMetadataReferences().Any()
                || change.GetAddedAnalyzerReferences().Any() || change.GetRemovedAnalyzerReferences().Any())
                throw new PublicInvalidOperationException("The preview changes project options or assembly/analyzer references without an authored physical representation.");
        }
    }

    private static bool CompilationOptionsMatchPhysicalDelta(ProjectChanges change)
    {
        var before = change.OldProject.CompilationOptions;
        var after = change.NewProject.CompilationOptions;
        if (Equals(before, after)) return true;
        if (before is null || after is null
            || !(change.GetAddedAnalyzerConfigDocuments().Any() || change.GetRemovedAnalyzerConfigDocuments().Any()
                || change.GetChangedAnalyzerConfigDocuments().Any())) return false;
        // Roslyn derives a new options provider when its analyzer-config documents
        // change. Rebind the complete original options through that project's own
        // cache; compare the resulting provider and every other option, rather
        // than discarding provider identity or accepting arbitrary option edits.
        var expected = change.NewProject.WithCompilationOptions(before).CompilationOptions;
        return Equals(expected, after);
    }

    private static IEnumerable<DocumentId> RelocatedDocuments(ProjectChanges change) =>
        change.GetChangedDocuments().Concat(change.GetChangedAdditionalDocuments()).Concat(change.GetChangedAnalyzerConfigDocuments())
            .Where(id => !FileSystemPath.Comparer.Equals(GetTextDocument(change.OldProject.Solution, id)?.FilePath,
                GetTextDocument(change.NewProject.Solution, id)?.FilePath));

    internal static bool RequiresPhysicalProjectRender(ProjectChanges change) => change.NewProject.FilePath is not null
        && (change.GetAddedProjectReferences().Any() || change.GetRemovedProjectReferences().Any()
            || change.GetAddedDocuments().Any() || change.GetRemovedDocuments().Any()
            || change.GetAddedAdditionalDocuments().Any() || change.GetRemovedAdditionalDocuments().Any()
            || change.GetAddedAnalyzerConfigDocuments().Any() || change.GetRemovedAnalyzerConfigDocuments().Any()
            || RelocatedDocuments(change).Any());

    internal static IEnumerable<(TextDocument? Old, TextDocument? New)> DocumentChanges(Solution original, Solution modified)
    {
        // Annotations can change Roslyn state without changing authored file intent.
        // Unknown/lazy linked text remains a delta; deciding this exemption performs no IO.
        var beforeByPath = IndexParticipants(original);
        var afterByPath = IndexParticipants(modified);
        foreach (var change in modified.GetChanges(original).GetProjectChanges())
        {
            var ids = change.GetChangedDocuments().Concat(change.GetAddedDocuments()).Concat(change.GetRemovedDocuments())
                .Concat(change.GetChangedAdditionalDocuments()).Concat(change.GetAddedAdditionalDocuments()).Concat(change.GetRemovedAdditionalDocuments())
                .Concat(change.GetChangedAnalyzerConfigDocuments()).Concat(change.GetAddedAnalyzerConfigDocuments()).Concat(change.GetRemovedAnalyzerConfigDocuments());
            foreach (var id in ids.Distinct())
            {
                var before = GetTextDocument(original, id);
                var after = GetTextDocument(modified, id);
                if (before is not null && after is not null
                    && before.Name == after.Name && before.Folders.SequenceEqual(after.Folders)
                    && DocumentKind(before) == DocumentKind(after)
                    && (before is not Document beforeSource || after is Document afterSource
                        && beforeSource.SourceCodeKind == afterSource.SourceCodeKind)
                    && FileSystemPath.Comparer.Equals(before.FilePath, after.FilePath)
                    && before.TryGetText(out var text)
                    && Matches(beforeByPath[before.FilePath ?? before.Name], text)
                    && Matches(afterByPath[after.FilePath ?? after.Name], text))
                    continue;
                yield return (before, after);
            }
        }
        foreach (var project in modified.GetChanges(original).GetAddedProjects())
            foreach (var document in AllDocuments(project))
                yield return (null, document);

        static Dictionary<string, TextDocument[]> IndexParticipants(Solution solution) => solution.Projects
            .SelectMany(AllDocuments).GroupBy(document => document.FilePath ?? document.Name, FileSystemPath.Comparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), FileSystemPath.Comparer);

        static bool Matches(IEnumerable<TextDocument> participants, Microsoft.CodeAnalysis.Text.SourceText baseline) =>
            participants.All(document => document.TryGetText(out var text)
                && Equals(baseline.Encoding, text.Encoding)
                && baseline.ChecksumAlgorithm == text.ChecksumAlgorithm
                && baseline.ContentEquals(text));
    }

    private static TextDocument? GetTextDocument(Solution solution, DocumentId id) =>
        (TextDocument?)solution.GetDocument(id) ?? solution.GetAdditionalDocument(id) ?? solution.GetAnalyzerConfigDocument(id);

    internal static IEnumerable<TextDocument> AllDocuments(Project project) =>
        project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments);

    internal static IReadOnlyList<string> ChangedPaths(Solution original, Solution modified)
    {
        // Potential write ownership is immutable snapshot intent. Never omit a boundary
        // check because mutable current disk bytes happen to make rendering a no-op.
        var paths = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var (oldDocument, newDocument) in DocumentChanges(original, modified))
        {
            if (oldDocument?.FilePath is { } oldPath) paths.Add(oldPath);
            if (newDocument?.FilePath is { } newPath) paths.Add(newPath);
        }
        var changes = modified.GetChanges(original);
        foreach (var change in changes.GetProjectChanges())
            if (RequiresPhysicalProjectRender(change))
                if (change.NewProject.FilePath is { } path) paths.Add(path);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
            if (original.FilePath is { } solutionPath) paths.Add(solutionPath);
        return paths.ToArray();
    }

    internal static async Task<IReadOnlyList<Mutation>> CreateAsync(
        Solution original, Solution modified, Func<string, CancellationToken, Task<byte[]?>> readBytes,
        CompilerReferenceAuthority references, bool includeDocuments, bool allowVirtualPaths, CancellationToken ct)
    {
        ValidateDelta(original, modified);
        var mutations = new Dictionary<string, Mutation>(FileSystemPath.Comparer);
        var evaluationGraphs = new Dictionary<Solution, EvaluatedBuildGraph.Graph>(ReferenceEqualityComparer.Instance);
        IReadOnlyDictionary<string, string> ResolveProperties(Project project)
        {
            if (!evaluationGraphs.TryGetValue(project.Solution, out var graph))
            {
                graph = EvaluatedBuildGraph.Source(project.Solution, ImmutableDictionary<string, byte[]>.Empty, ct);
                evaluationGraphs.Add(project.Solution, graph);
            }
            return EvaluatedBuildGraph.Resolve(project, graph).Globals;
        }
        if (includeDocuments)
        {
            var deltas = DocumentChanges(original, modified).ToArray();
            var paths = deltas.SelectMany(delta => new[] { delta.Old, delta.New }).OfType<TextDocument>()
                .Select(document => document.FilePath ?? (allowVirtualPaths ? document.Name
                    : throw new PublicInvalidOperationException("The preview changes a document without a physical file path.")))
                .Distinct(FileSystemPath.Comparer);
            var beforeDocuments = IndexDocuments(original);
            var afterDocuments = IndexDocuments(modified);
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var oldText = await ReadTextAsync(beforeDocuments.GetValueOrDefault(path)).ConfigureAwait(false);
                var newText = await ReadTextAsync(afterDocuments.GetValueOrDefault(path)).ConfigureAwait(false);
                if (oldText is not null && newText is not null && oldText.ContentEquals(newText)) continue;
                var before = await readBytes(path, ct).ConfigureAwait(false);
                if (!allowVirtualPaths)
                {
                    var diskText = before is null ? null : CsprojSemanticEquality.CreateSnapshot(before).Content;
                    if (oldText is null && before is not null && newText?.ToString() == diskText) continue;
                    if (oldText is null ? before is not null : diskText is null || oldText.ToString() != diskText)
                        throw new PublicInvalidOperationException("A source file differs from the loaded snapshot; reload before applying the preview.");
                }
                var encoding = before is null ? SourceFileEncoding.FromSourceText(newText?.Encoding) : SourceFileEncoding.FromBytes(before);
                var after = newText is null ? null : Encode(newText.ToString(), encoding);
                mutations[path] = new Mutation(path, before, after, oldText?.ToString() ?? "", newText?.ToString() ?? "");
            }

            Dictionary<string, TextDocument[]> IndexDocuments(Solution solution) => solution.Projects
                .SelectMany(AllDocuments).Where(document => document.FilePath is not null || allowVirtualPaths)
                .GroupBy(document => document.FilePath ?? document.Name, FileSystemPath.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), FileSystemPath.Comparer);

            async Task<Microsoft.CodeAnalysis.Text.SourceText?> ReadTextAsync(TextDocument[]? documents)
            {
                if (documents is null || documents.Length == 0) return null;
                var text = await documents[0].GetTextAsync(ct).ConfigureAwait(false);
                foreach (var document in documents.Skip(1))
                    if (!text.ContentEquals(await document.GetTextAsync(ct).ConfigureAwait(false)))
                        throw new PublicInvalidOperationException("The reviewed documents prescribe conflicting content for one physical file.");
                return text;
            }
        }
        foreach (var change in modified.GetChanges(original).GetProjectChanges())
        {
            var referenceChange = change.GetAddedProjectReferences().Any() || change.GetRemovedProjectReferences().Any();
            if (!RequiresPhysicalProjectRender(change))
            {
                if (referenceChange) throw new PublicInvalidOperationException("Project-reference changes require an existing project file.");
                continue;
            }
            var path = change.NewProject.FilePath;
            if (path is null)
            {
                if (referenceChange) throw new PublicInvalidOperationException("Project-reference changes require an existing project file.");
                continue;
            }
            references.AssertCovers(change.OldProject);
            var bytes = await readBytes(path, ct).ConfigureAwait(false);
            if (bytes is null)
            {
                if (allowVirtualPaths && !referenceChange) continue;
                throw new PublicInvalidOperationException("The changed project file is missing; its physical graph cannot be rendered.");
            }
            var renderBase = mutations.TryGetValue(path, out var authored) ? authored.After
                ?? throw new PublicInvalidOperationException("A project file cannot be deleted while its graph is being mutated.") : bytes;
            var after = RenderProjectFile(change, original, modified, renderBase, references, ResolveProperties);
            if (!bytes.AsSpan().SequenceEqual(after))
                mutations[path] = new Mutation(path, bytes, after, CsprojSemanticEquality.CreateSnapshot(bytes).Content,
                    CsprojSemanticEquality.CreateSnapshot(after).Content);
        }
        var solutionChanges = modified.GetChanges(original);
        if ((solutionChanges.GetAddedProjects().Any() || solutionChanges.GetRemovedProjects().Any())
            && !await UsesProjectEntryMembershipAsync(original, modified, allowVirtualPaths, readBytes, ct).ConfigureAwait(false))
        {
            var path = original.FilePath ?? throw new PublicInvalidOperationException("Project membership changes require an on-disk solution file.");
            WorkspaceManager.EnsurePhysicalPath(path);
            var before = await readBytes(path, ct).ConfigureAwait(false)
                ?? throw new PublicInvalidOperationException("The solution file is missing.");
            var renderBase = mutations.TryGetValue(path, out var authored) ? authored.After
                ?? throw new PublicInvalidOperationException("A solution file cannot be deleted while its membership is being mutated.") : before;
            await RequireAddedProjectFilesAsync(modified.GetChanges(original), readBytes, ct).ConfigureAwait(false);
            var after = RenderSolutionMembership(original, modified, path, renderBase);
            mutations[path] = new Mutation(path, before, after, CsprojSemanticEquality.CreateSnapshot(before).Content,
                CsprojSemanticEquality.CreateSnapshot(after).Content);
        }
        return mutations.Values.ToArray();
    }

    internal static Solution WithPhysicalDocumentText(Solution expected, IReadOnlyList<Mutation> mutations)
    {
        var byPath = mutations.ToDictionary(mutation => mutation.Path, FileSystemPath.Comparer);
        var result = expected;
        foreach (var document in expected.Projects.SelectMany(AllDocuments))
        {
            if (document.FilePath is not { } path || !byPath.TryGetValue(path, out var mutation) || mutation.After is null) continue;
            var text = Microsoft.CodeAnalysis.Text.SourceText.From(mutation.NewText, SourceFileEncoding.FromBytes(mutation.After));
            result = DocumentKind(document) switch
            {
                "Compile" => result.WithDocumentText(document.Id, text),
                "AdditionalFiles" => result.WithAdditionalDocumentText(document.Id, text),
                _ => result.WithAnalyzerConfigDocumentText(document.Id, text)
            };
        }
        return result;
    }

    internal static byte[] Encode(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static byte[] RenderProjectFile(ProjectChanges change, Solution original, Solution modified, byte[] bytes,
        CompilerReferenceAuthority references, Func<Project, IReadOnlyDictionary<string, string>> resolveProperties)
    {
        references.AssertCurrent(change.OldProject);
        IReadOnlyDictionary<string, string>? oldProperties = null;
        IReadOnlyDictionary<string, string>? newProperties = null;
        IReadOnlyDictionary<string, string> OldProperties() => oldProperties ??= resolveProperties(change.OldProject);
        IReadOnlyDictionary<string, string> NewProperties() => newProperties ??= resolveProperties(change.NewProject);
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);
        var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new PublicInvalidOperationException("The project XML has no root.");
        var path = change.NewProject.FilePath ?? throw new PublicInvalidOperationException("The project path is missing.");
        var directory = Path.GetDirectoryName(path)!;
        var patches = new List<(int Start, int Length, string Text)>();
        var appended = new List<XElement>();
        var added = change.GetAddedProjectReferences().ToArray();
        var removed = change.GetRemovedProjectReferences().ToArray();
        var handled = new HashSet<ProjectId>();
        var evaluated = removed.Length == 0 ? [] : EvaluatedProjectGraph.References(change.OldProject, OldProperties());
        foreach (var reference in removed)
        {
            if (references.IsImplicit(change.OldProject, reference))
                throw new PublicInvalidOperationException("An SDK-generated compiler reference must be changed through its authored dependency or transitivity policy.");
            var target = original.GetProject(reference.ProjectId)?.FilePath
                ?? throw new PublicInvalidOperationException("A removed reference has no physical target.");
            var elements = root.Descendants().Where(element => element.Name.LocalName == "ProjectReference"
                && ProjectReferenceTargetsPath((string?)element.Attribute("Include"), directory, target)).ToArray();
            var replacement = added.SingleOrDefault(candidate => candidate.ProjectId == reference.ProjectId);
            if (elements.Length != 1)
            {
                var provenance = evaluated.Where(item => FileSystemPath.Comparer.Equals(item.TargetPath, target)).ToArray();
                if (provenance.Length != 1)
                    throw new PublicInvalidOperationException("The changed reference has ambiguous evaluated provenance.");
                var item = provenance[0];
                var sameDefinition = evaluated.Count(candidate => FileSystemPath.Comparer.Equals(candidate.DefiningPath, item.DefiningPath)
                    && candidate.Line == item.Line);
                if (FileSystemPath.Comparer.Equals(item.DefiningPath, path) && sameDefinition == 1)
                    elements = root.Descendants().Where(element => element.Name.LocalName == "ProjectReference"
                        && ((IXmlLineInfo)element).LineNumber == item.Line).ToArray();
                if (elements.Length != 1)
                {
                    // Override only this evaluated target in the owning project; never mutate
                    // a shared import or a wildcard item that also supplies sibling edges.
                    var itemOverride = new XElement(root.Name.Namespace + "ProjectReference",
                        new XAttribute(replacement is null ? "Remove" : "Update", EscapeMsBuildLiteral(Path.GetRelativePath(directory, target))));
                    if (replacement is not null)
                    {
                        SetMetadata(itemOverride, "Aliases", string.Join(",", replacement.Aliases));
                        SetMetadata(itemOverride, "EmbedInteropTypes", replacement.EmbedInteropTypes ? "true" : "false");
                        handled.Add(reference.ProjectId);
                    }
                    appended.Add(itemOverride);
                    continue;
                }
            }
            var element = elements[0];
            var span = ElementSpan(snapshot.Content, element);
            if (replacement is null)
                patches.Add((span.Start, span.Length, ""));
            else
            {
                var updated = new XElement(element);
                SetMetadata(updated, "Aliases", string.Join(",", replacement.Aliases));
                SetMetadata(updated, "EmbedInteropTypes", replacement.EmbedInteropTypes ? "true" : "false");
                patches.Add((span.Start, span.Length, SerializeFragment(updated, element.Parent!)));
                handled.Add(reference.ProjectId);
            }
        }
        foreach (var reference in added.Where(reference => !handled.Contains(reference.ProjectId)))
        {
            var target = modified.GetProject(reference.ProjectId)?.FilePath
                ?? throw new PublicInvalidOperationException("An added reference has no physical target.");
            var item = new XElement(root.Name.Namespace + "ProjectReference", new XAttribute("Include", EscapeMsBuildLiteral(Path.GetRelativePath(directory, target))));
            SetMetadata(item, "Aliases", string.Join(",", reference.Aliases));
            SetMetadata(item, "EmbedInteropTypes", reference.EmbedInteropTypes ? "true" : "false");
            appended.Add(item);
        }
        var relocated = RelocatedDocuments(change).ToArray();
        AddDocumentMembership(change.GetAddedDocuments().Concat(relocated.Where(id => original.GetDocument(id) is not null)),
            change.GetRemovedDocuments().Concat(relocated.Where(id => original.GetDocument(id) is not null)), "Compile");
        AddDocumentMembership(change.GetAddedAdditionalDocuments().Concat(relocated.Where(id => original.GetAdditionalDocument(id) is not null)),
            change.GetRemovedAdditionalDocuments().Concat(relocated.Where(id => original.GetAdditionalDocument(id) is not null)), "AdditionalFiles");
        AddDocumentMembership(change.GetAddedAnalyzerConfigDocuments().Concat(relocated.Where(id => original.GetAnalyzerConfigDocument(id) is not null)),
            change.GetRemovedAnalyzerConfigDocuments().Concat(relocated.Where(id => original.GetAnalyzerConfigDocument(id) is not null)), "EditorConfigFiles");
        var rendered = ApplyXmlPatches(snapshot, document, patches, appended, wrapItemGroup: true);
        if (added.Length > 0 || removed.Length > 0)
        {
            var expectedReferences = change.NewProject.ProjectReferences.Where(reference => !references.IsImplicit(change.OldProject, reference))
                .Select(reference => ReferenceKey(modified, reference, static target => target)).ToHashSet(StringComparer.Ordinal);
            var actualReferences = EvaluatedProjectGraph.References(change.NewProject, NewProperties(), rendered).Select(item =>
                EvaluatedReferenceKey(item)).ToHashSet(StringComparer.Ordinal);
            if (!expectedReferences.SetEquals(actualReferences))
                throw new PublicInvalidOperationException("The reference mutation cannot be represented faithfully in this evaluated project definition.");
        }
        return rendered;

        void AddDocumentMembership(IEnumerable<DocumentId> additions, IEnumerable<DocumentId> removals, string itemName)
        {
            foreach (var id in removals)
            {
                var target = GetTextDocument(original, id)?.FilePath;
                if (target is null) continue;
                foreach (var element in root.Descendants().Where(element => element.Name.LocalName == itemName
                             && ProjectReferenceTargetsPath((string?)element.Attribute("Include"), directory, target)))
                {
                    var span = ElementSpan(snapshot.Content, element);
                    patches.Add((span.Start, span.Length, ""));
                }
                var deletedPhysicalFile = !modified.Projects.SelectMany(AllDocuments)
                    .Any(document => FileSystemPath.Comparer.Equals(document.FilePath, target));
                if (itemName != "Compile" || !deletedPhysicalFile || !EvaluatedProjectGraph.IncludedByCompileGlob(change.OldProject, OldProperties(), target))
                    appended.Add(new XElement(root.Name.Namespace + itemName, new XAttribute("Remove", EscapeMsBuildLiteral(Path.GetRelativePath(directory, target)))));
            }
            foreach (var id in additions)
            {
                var addedDocument = GetTextDocument(modified, id);
                var target = addedDocument?.FilePath;
                if (target is null) throw new PublicInvalidOperationException("A new document has no physical path.");
                var relative = EscapeMsBuildLiteral(Path.GetRelativePath(directory, target));
                var standardImplicit = itemName == "Compile" && EvaluatedProjectGraph.IncludedByCompileGlob(change.NewProject, NewProperties(), target);
                if (standardImplicit) continue;
                appended.Add(new XElement(root.Name.Namespace + itemName, new XAttribute("Remove", relative)));
                var item = new XElement(root.Name.Namespace + itemName, new XAttribute("Include", relative));
                if (itemName == "Compile" && !FileSystemPath.IsStrictDescendant(directory, target))
                    item.Add(new XElement(root.Name.Namespace + "Link", EscapeMsBuildLiteral(Path.Combine([.. addedDocument!.Folders, addedDocument.Name]))));
                appended.Add(item);
            }
        }
    }

    private static void SetMetadata(XElement element, string name, string? value)
    {
        element.Attributes().Where(attribute => attribute.Name.LocalName == name).Remove();
        element.Elements().Where(child => child.Name.LocalName == name).Remove();
        // The alias list uses comma delimiters; Escape preserves those delimiters
        // while preventing literal metadata from becoming an MSBuild expression.
        if (value is not null) element.Add(new XElement(element.Name.Namespace + name, EscapeMsBuildLiteral(value)));
    }

    private static string EscapeMsBuildLiteral(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);

    internal static bool ProjectReferenceTargetsPath(string? include, string directory, string target)
    {
        if (string.IsNullOrWhiteSpace(include) || include.Contains("$(", StringComparison.Ordinal)
            || include.Contains("@(", StringComparison.Ordinal) || include.Contains("%(", StringComparison.Ordinal)
            || include.IndexOfAny(['*', '?', ';']) >= 0) return false;
        // Reject syntax in its authored spelling, then decode exactly once.
        // Native item path handling preserves literal percent sequences and
        // interprets Windows separators on every supported host.
        try { return FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(NativeItemPath.FullPath(
            Microsoft.Build.Evaluation.ProjectCollection.Unescape(include), directory)), PhysicalPathResolver.Resolve(target)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool SolutionProjectTargetsPath(string? path, string directory, string target)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // Solution Path attributes are literal solution-format paths, not item specs.
        try { return FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(NativeItemPath.FullPath(path, directory)), PhysicalPathResolver.Resolve(target)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string SerializeFragment(XElement element, XElement parent)
    {
        // Serialize in the inherited namespace context. Standalone serialization adds
        // xmlns attributes to MSBuild property/item fragments, which its parser rejects.
        var bindings = parent.AncestorsAndSelf().SelectMany(ancestor => ancestor.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            .GroupBy(attribute => attribute.Name).Select(group => new XAttribute(group.First())).ToArray();
        var context = new XElement(parent.Name.Namespace + "FragmentContext", bindings, new XElement(element));
        var serialized = context.ToString(SaveOptions.DisableFormatting);
        return serialized[(serialized.IndexOf('>') + 1)..serialized.LastIndexOf("</", StringComparison.Ordinal)];
    }

    private static byte[] ApplyXmlPatches(CsprojSemanticEquality.ProjectFileSnapshot snapshot, XDocument document,
        List<(int Start, int Length, string Text)> patches, IReadOnlyList<XElement> appended, bool wrapItemGroup)
    {
        if (appended.Count > 0)
        {
            var root = document.Root!;
            var newline = DefinitionNewline(snapshot.Content);
            var addition = wrapItemGroup ? SerializeFragment(new XElement(root.Name.Namespace + "ItemGroup", appended), root)
                : string.Join(newline + "  ", appended.Select(element => SerializeFragment(element, root)));
            var rootSpan = ElementSpan(snapshot.Content, root);
            var close = snapshot.Content.LastIndexOf("</", rootSpan.Start + rootSpan.Length - 1, StringComparison.Ordinal);
            if (close < rootSpan.Start)
            {
                var prefix = root.GetPrefixOfNamespace(root.Name.Namespace);
                var name = string.IsNullOrEmpty(prefix) ? root.Name.LocalName : prefix + ":" + root.Name.LocalName;
                var terminator = rootSpan.Start + rootSpan.Length - 2;
                patches.Add((terminator, 2, ">" + newline + "  " + addition + newline + "</" + name + ">"));
            }
            else patches.Add((close, 0, "  " + addition + newline));
        }
        var text = snapshot.Content;
        foreach (var patch in patches.OrderByDescending(patch => patch.Start))
            text = text.Remove(patch.Start, patch.Length).Insert(patch.Start, patch.Text);
        var encoding = snapshot.HasPreamble ? snapshot.TextEncoding : SourceFileEncoding.FromBytes(snapshot.Bytes);
        return Encode(text, encoding);
    }

    private static (int Start, int Length) ElementSpan(string text, XElement element)
    {
        var info = (IXmlLineInfo)element;
        var lines = DefinitionLines(text).Skip(info.LineNumber - 1).Take(1).ToArray();
        if (!info.HasLineInfo() || info.LineNumber < 1 || info.LinePosition < 1 || lines.Length != 1
            || info.LinePosition > lines[0].ContentLength)
            throw new PublicInvalidOperationException("The XML element has no faithful authored position.");
        var start = lines[0].Start + info.LinePosition - 1;
        if (text[start] != '<')
        {
            if (start == lines[0].Start || text[start - 1] != '<')
                throw new PublicInvalidOperationException("The XML element position does not select its authored opening tag.");
            start--;
        }
        var cursor = start;
        var depth = 0;
        do
        {
            var open = text.IndexOf('<', cursor);
            if (text.AsSpan(open).StartsWith("<!--", StringComparison.Ordinal))
            { cursor = text.IndexOf("-->", open, StringComparison.Ordinal) + 3; continue; }
            if (text.AsSpan(open).StartsWith("<![CDATA[", StringComparison.Ordinal))
            { cursor = text.IndexOf("]]>", open, StringComparison.Ordinal) + 3; continue; }
            char quote = '\0';
            var end = open + 1;
            for (; end < text.Length; end++)
            {
                var character = text[end];
                if (quote != '\0') { if (character == quote) quote = '\0'; }
                else if (character is '\'' or '"') quote = character;
                else if (character == '>') break;
            }
            if (text[open + 1] == '/') depth--;
            else if (text[end - 1] != '/' && text[open + 1] is not '?' and not '!') depth++;
            cursor = end + 1;
        } while (depth > 0);
        return (start, cursor - start);
    }

    private static IEnumerable<(int Start, int ContentLength, int Length)> DefinitionLines(string text)
    {
        var start = 0;
        for (var cursor = 0; cursor < text.Length; cursor++)
        {
            if (text[cursor] is not '\r' and not '\n') continue;
            var contentLength = cursor - start;
            if (text[cursor] == '\r' && cursor + 1 < text.Length && text[cursor + 1] == '\n') cursor++;
            yield return (start, contentLength, cursor + 1 - start);
            start = cursor + 1;
        }
        yield return (start, text.Length - start, text.Length - start);
    }

    private static string DefinitionNewline(string text)
    {
        foreach (var line in DefinitionLines(text))
            if (line.Length != line.ContentLength)
                return text.Substring(line.Start + line.ContentLength, line.Length - line.ContentLength);
        return "\n";
    }

    private static async Task<bool> UsesProjectEntryMembershipAsync(Solution original, Solution modified, bool allowVirtualPaths,
        Func<string, CancellationToken, Task<byte[]?>> readBytes, CancellationToken ct)
    {
        if (allowVirtualPaths && original.Workspace is AdhocWorkspace
            && (original.FilePath is null || await readBytes(original.FilePath, ct).ConfigureAwait(false) is null)) return true;
        if (original.FilePath is { } filePath && !filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return false;
        var referenced = original.Projects.SelectMany(project => project.ProjectReferences).Select(reference => reference.ProjectId).ToHashSet();
        var roots = original.Projects.Where(project => !referenced.Contains(project.Id)).ToArray();
        if (roots.Length != 1 || modified.GetProject(roots[0].Id) is not { } root)
            throw new PublicInvalidOperationException("The standalone project entry cannot represent this project membership.");
        var reachable = modified.GetProjectDependencyGraph().GetProjectsThatThisProjectTransitivelyDependsOn(root.Id).Append(root.Id).ToHashSet();
        if (!reachable.SetEquals(modified.ProjectIds))
            throw new PublicInvalidOperationException("A standalone project cannot enroll an unreferenced solution member.");
        await RequireAddedProjectFilesAsync(modified.GetChanges(original), readBytes, ct).ConfigureAwait(false);
        return true;
    }

    private static async Task RequireAddedProjectFilesAsync(SolutionChanges changes,
        Func<string, CancellationToken, Task<byte[]?>> readBytes, CancellationToken ct)
    {
        foreach (var project in changes.GetAddedProjects())
            if (project.FilePath is null || await readBytes(project.FilePath, ct).ConfigureAwait(false) is null)
                throw new PublicInvalidOperationException("An added project requires an existing authored project file.");
    }

    private static byte[] RenderSolutionMembership(Solution original, Solution modified, string path, byte[] bytes)
    {
        var changes = modified.GetChanges(original);
        var added = changes.GetAddedProjects().ToArray();
        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return RenderTextSolutionMembership(original, modified, path, bytes);
        if (!path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            throw new PublicInvalidOperationException("The project membership cannot be represented by this solution definition.");
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);
        var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var directory = Path.GetDirectoryName(path)!;
        var patches = new List<(int Start, int Length, string Text)>();
        foreach (var project in changes.GetRemovedProjects())
            foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Project"
                         && SolutionProjectTargetsPath((string?)element.Attribute("Path"), directory, project.FilePath!)))
            {
                var span = ElementSpan(snapshot.Content, element);
                patches.Add((span.Start, span.Length, ""));
            }
        var additions = added.Select(project => new XElement(document.Root!.Name.Namespace + "Project",
            new XAttribute("Path", Path.GetRelativePath(directory, project.FilePath!)))).ToArray();
        return ApplyXmlPatches(snapshot, document, patches, additions, wrapItemGroup: false);
    }

    private static byte[] RenderTextSolutionMembership(Solution original, Solution modified, string path, byte[] bytes)
    {
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);
        var text = snapshot.Content;
        var newline = DefinitionNewline(text);
        var directory = Path.GetDirectoryName(path)!;
        var changes = modified.GetChanges(original);
        var removedPaths = changes.GetRemovedProjects().Select(project => project.FilePath).ToHashSet(FileSystemPath.Comparer);
        var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        var configurations = new List<string>();
        var inConfigurations = false;
        var skippingProject = false;
        foreach (var span in DefinitionLines(text))
        {
            var line = text.Substring(span.Start, span.Length);
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                var fields = trimmed.Split('"');
                if (fields.Length < 8) throw new PublicInvalidOperationException("The solution project entry is not representable.");
                var include = fields[5];
                var projectPath = NativeItemPath.FullPath(include, directory);
                skippingProject = removedPaths.Contains(projectPath);
                if (skippingProject) removedIds.Add(fields[7]);
            }
            if (!skippingProject) lines.Add(line);
            if (trimmed == "EndProject") skippingProject = false;
            if (trimmed.StartsWith("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.Ordinal)) inConfigurations = true;
            else if (trimmed == "EndGlobalSection") inConfigurations = false;
            else if (inConfigurations && trimmed.Contains('=')) configurations.Add(trimmed.Split('=')[0].Trim());
        }
        lines.RemoveAll(line => removedIds.Any(id => line.TrimStart().StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)
            || line.TrimStart().StartsWith(id + " =", StringComparison.OrdinalIgnoreCase)
            || line.TrimEnd().EndsWith("= " + id, StringComparison.OrdinalIgnoreCase)));
        var globalIndex = lines.FindIndex(line => line.Trim() == "Global");
        if (globalIndex < 0) throw new PublicInvalidOperationException("The solution has no global section.");
        var configurationRows = new List<string>();
        foreach (var project in changes.GetAddedProjects())
        {
            var normalized = Path.GetFullPath(project.FilePath!);
            if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
            var id = new Guid(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 16)).ToString("B").ToUpperInvariant();
            lines.InsertRange(globalIndex, ["Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"" + project.Name + "\", \""
                + Path.GetRelativePath(directory, project.FilePath!) + "\", \"" + id + "\"" + newline, "EndProject" + newline]);
            globalIndex += 2;
            foreach (var configuration in configurations)
            {
                configurationRows.Add("\t\t" + id + "." + configuration + ".ActiveCfg = " + configuration + newline);
                configurationRows.Add("\t\t" + id + "." + configuration + ".Build.0 = " + configuration + newline);
            }
        }
        if (configurationRows.Count > 0)
        {
            var section = lines.FindIndex(line => line.Trim().StartsWith("GlobalSection(ProjectConfigurationPlatforms)", StringComparison.Ordinal));
            if (section < 0)
            {
                lines.InsertRange(globalIndex + 1, ["\tGlobalSection(ProjectConfigurationPlatforms) = postSolution" + newline, .. configurationRows, "\tEndGlobalSection" + newline]);
            }
            else
            {
                var end = lines.FindIndex(section + 1, line => line.Trim() == "EndGlobalSection");
                if (end < 0) throw new PublicInvalidOperationException("The solution configuration section is incomplete.");
                lines.InsertRange(end, configurationRows);
            }
        }
        return Encode(string.Concat(lines), SourceFileEncoding.FromBytes(bytes));
    }

    internal static byte[] RelocateDefinition(string definitionPath, byte[] bytes, Func<string, string> mapPath,
        Func<string, string> mapDefinitionPath, IReadOnlySet<string> pathProperties, IReadOnlyList<BuildPathRoles.Declaration> activePaths)
        => RelocateDefinitionCore(definitionPath, bytes, mapPath, mapDefinitionPath, pathProperties, activePaths, absoluteMembership: false);

    internal static byte[] RenderSolutionCoordinates(string definitionPath, byte[] bytes, Func<string, string> mapPath)
        => RelocateDefinitionCore(definitionPath, bytes, mapPath, mapPath, BuildPathRelocation.KnownPathProperties, [], absoluteMembership: true);

    private static byte[] RelocateDefinitionCore(string definitionPath, byte[] bytes, Func<string, string> mapPath,
        Func<string, string> mapDefinitionPath, IReadOnlySet<string> pathProperties, IReadOnlyList<BuildPathRoles.Declaration> activePaths, bool absoluteMembership)
    {
        definitionPath = Path.GetFullPath(definitionPath);
        string MembershipPath(string value) => absoluteMembership || NativeItemPath.IsPathFullyQualified(value)
            ? mapPath(NativeItemPath.FullPath(value, Path.GetDirectoryName(definitionPath)!)) : value;
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);
        if (definitionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = string.Concat(DefinitionLines(snapshot.Content).Select(line => System.Text.RegularExpressions.Regex.Replace(
                snapshot.Content.Substring(line.Start, line.Length),
                @"^(Project\([^\r\n]+?=\s*""[^""]*"",\s*"")([^""]+)("")",
                match => match.Groups[1].Value + (match.Groups[1].Value.Contains("2150E333-8FDC-42A3-9474-1A3956D46DE8", StringComparison.OrdinalIgnoreCase)
                    ? match.Groups[2].Value : MembershipPath(match.Groups[2].Value)) + match.Groups[3].Value)));
            return Encode(rewritten, SourceFileEncoding.FromBytes(bytes));
        }
        var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var originalElements = document.Root!.DescendantsAndSelf().ToArray();
        var clone = new XDocument(document);
        var clonedElements = clone.Root!.DescendantsAndSelf().ToArray();
        var changed = new HashSet<XElement>();
        for (var i = 0; i < originalElements.Length; i++)
        {
            var element = clonedElements[i];
            var slnx = definitionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
            // Import operands and their guarding predicates identify definition
            // provenance. Other consumers retain private item coordinates, even
            // when the same package file also participates in an import.
            var mapElementPath = !slnx && element.Name.LocalName is "Import" or "ImportGroup"
                ? mapDefinitionPath : mapPath;
            if (slnx && element.Name.LocalName == "Project" && element.Attribute("Path") is { } membership
                && MembershipPath(membership.Value) != membership.Value && element.Attribute("Id") is null)
            {
                // The official serializer derives a missing Id from the authored path
                // spelling. Transport must preserve that identity before changing Path.
                element.Add(new XAttribute("Id", SolutionProjectId(membership.Value)));
                changed.Add(originalElements[i]);
            }
            if (BuildPathRelocation.IsPackageProperty(element.Name.LocalName)) continue;
            foreach (var consumers in activePaths.Where(consumer => FileSystemPath.Comparer.Equals(consumer.FilePath, definitionPath)
                         && consumer.Ordinal == i && consumer.Name == element.Name.LocalName).GroupBy(consumer => consumer.Attribute))
            {
                var current = consumers.Key is null ? element.Value : (string?)element.Attribute(consumers.Key);
                var mapped = consumers.Where(consumer => consumer.Expression == current)
                    .Select(consumer => (consumer.Expanded, Relocated: consumer.Attribute == "Condition"
                        ? BuildPathRelocation.RelocateCapturedCondition(consumer.Expanded, mapElementPath, pathProperties, consumer.ExistsOperands)
                        : BuildPathRelocation.RelocateMsBuildValue(consumer.Expanded, mapElementPath)))
                    .Where(value => value.Expanded != value.Relocated).Select(value => value.Relocated).Distinct(StringComparer.Ordinal).ToArray();
                if (mapped.Length > 1)
                    throw new PublicInvalidOperationException("An active physical path declaration has incompatible transport expressions across evaluated contexts.");
                if (mapped.Length == 0) continue;
                if (consumers.Key is null) element.Value = mapped[0];
                else element.SetAttributeValue(consumers.Key, mapped[0]);
                changed.Add(originalElements[i]);
            }
            // Evaluation-time declarations are transported only through native
            // executed evidence above. Runtime targets are not evaluated yet;
            // retain their explicit path-slot transport and structural membership.
            var runtimeDeclaration = element.AncestorsAndSelf().Any(ancestor => ancestor.Name.LocalName is "Target" or "UsingTask");
            var runtimeCondition = element.AncestorsAndSelf().Any(ancestor => ancestor.Name.LocalName == "Target");
            foreach (var attribute in element.Attributes().Where(attribute => (slnx || runtimeDeclaration) && (BuildPathRelocation.IsPathAttribute(attribute)
                || slnx && attribute.Parent?.Name.LocalName == "BuildDependency" && attribute.Name.LocalName == "Project"
                || runtimeCondition && BuildPathRelocation.IsPathCondition(attribute, pathProperties))))
            {
                var relocated = attribute.Name.LocalName == "Condition"
                    ? BuildPathRelocation.RelocateCondition(attribute.Value, mapElementPath, pathProperties)
                    : slnx && (attribute.Parent?.Name.LocalName == "Project" && attribute.Name.LocalName == "Path"
                        || attribute.Parent?.Name.LocalName == "BuildDependency" && attribute.Name.LocalName == "Project")
                        ? MembershipPath(attribute.Value)
                        : BuildPathRelocation.RelocateMsBuildValue(attribute.Value, mapElementPath);
                if (relocated != attribute.Value) { attribute.Value = relocated; changed.Add(originalElements[i]); }
            }
            foreach (var text in element.Nodes().OfType<XText>().Where(_ => runtimeDeclaration && BuildPathRelocation.IsPathText(element, pathProperties)))
            {
                var relocated = BuildPathRelocation.RelocateMsBuildValue(text.Value, mapPath);
                if (relocated != text.Value) { text.Value = relocated; changed.Add(originalElements[i]); }
            }
        }
        var patches = new List<(int Start, int Length, string Text)>();
        for (var i = 0; i < originalElements.Length; i++)
        {
            var element = originalElements[i];
            if (!changed.Contains(element) || element.Ancestors().Any(changed.Contains)) continue;
            var span = ElementSpan(snapshot.Content, element);
            patches.Add((span.Start, span.Length, SerializeFragment(clonedElements[i], originalElements[i].Parent ?? originalElements[i])));
        }
        return ApplyXmlPatches(snapshot, document, patches, [], wrapItemGroup: true);

    }

    internal static async Task AssertEquivalentAsync(Solution expected, Solution actual, Func<string, string> mapPath, Func<string, string> mapActual, bool verifyDocuments, IReadOnlySet<string> pathProperties, CancellationToken ct)
    {
        var expectedProjects = IndexProjects(expected, mapPath);
        var actualProjects = IndexProjects(actual, mapActual);
        EvaluatedBuildGraph.Graph? expectedGraph = null;
        if (!expectedProjects.Keys.ToHashSet(FileSystemPath.Comparer).SetEquals(actualProjects.Keys))
            throw new PhysicalGraphMismatchException("The evaluated physical project set differs from the reviewed graph.");
        foreach (var (path, project) in expectedProjects)
        {
            var loaded = actualProjects[path];
            var expectedReferences = project.ProjectReferences.Select(reference => ReferenceContextKey(expected, reference, mapPath)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var actualReferences = loaded.ProjectReferences.Select(reference => ReferenceContextKey(actual, reference, mapActual)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (!expectedReferences.SequenceEqual(actualReferences))
                throw new PhysicalGraphMismatchException("The evaluated physical project references differ from the reviewed graph.");
            if (!verifyDocuments) continue;
            if ((project.OutputFilePath is null) != (loaded.OutputFilePath is null)
                || project.OutputFilePath is { } output && !FileSystemPath.Comparer.Equals(mapPath(output), loaded.OutputFilePath is { } actualOutput ? mapActual(actualOutput) : null))
                throw new PhysicalGraphMismatchException("The evaluated project output path differs from the reviewed graph.");
            if (project.Name != loaded.Name || project.AssemblyName != loaded.AssemblyName || project.Language != loaded.Language
                || !Equals(project.ParseOptions, loaded.ParseOptions)
                || !Equals(NormalizeOptions(project.CompilationOptions, mapPath),
                    NormalizeOptions(loaded.CompilationOptions, mapActual)))
                throw new PhysicalGraphMismatchException("The evaluated compiler configuration differs from the reviewed graph.");
            var expectedMetadata = project.MetadataReferences.Select(reference => MetadataIdentity(reference, mapPath))
                .OrderBy(identity => identity.Key, StringComparer.Ordinal).ToArray();
            var actualMetadata = loaded.MetadataReferences.Select(reference => MetadataIdentity(reference, mapActual))
                .OrderBy(identity => identity.Key, StringComparer.Ordinal).ToArray();
            var expectedMetadataKeys = expectedMetadata.Select(identity => identity.Key).ToArray();
            var actualMetadataKeys = actualMetadata.Select(identity => identity.Key).ToArray();
            var expectedAnalyzers = project.AnalyzerReferences.Select(reference => AnalyzerKey(reference, mapPath))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var actualAnalyzers = loaded.AnalyzerReferences.Select(reference => AnalyzerKey(reference, mapActual))
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (!expectedMetadataKeys.SequenceEqual(actualMetadataKeys) || !expectedAnalyzers.SequenceEqual(actualAnalyzers))
            {
                var failure = new PhysicalGraphMismatchException("The evaluated assembly or analyzer references differ from the reviewed graph.");
                failure.Data["MetadataReferenceCounts"] = expectedMetadata.Length + "/" + actualMetadata.Length;
                var expectedNames = expectedMetadata.ToLookup(identity => identity.Key, identity => identity.FileName, StringComparer.Ordinal);
                var actualNames = actualMetadata.ToLookup(identity => identity.Key, identity => identity.FileName, StringComparer.Ordinal);
                failure.Data["MissingMetadataFileNames"] = string.Join(",", expectedMetadataKeys.Except(actualMetadataKeys, StringComparer.Ordinal).Select(key => expectedNames[key].First()));
                failure.Data["ExtraMetadataFileNames"] = string.Join(",", actualMetadataKeys.Except(expectedMetadataKeys, StringComparer.Ordinal).Select(key => actualNames[key].First()));
                failure.Data["MissingAnalyzerFileNames"] = string.Join(",", expectedAnalyzers.Except(actualAnalyzers).Select(Path.GetFileName));
                failure.Data["ExtraAnalyzerFileNames"] = string.Join(",", actualAnalyzers.Except(expectedAnalyzers).Select(Path.GetFileName));
                throw failure;
            }
            // Native glob and explicit item origins can expose multiple documents
            // at one physical path. Preserve each occurrence and its order; path
            // normalization must neither collapse copies nor accept extra ones.
            var documents = AllDocuments(loaded).Where(document => document.FilePath is not null)
                .GroupBy(document => DocumentKind(document) + "|" + mapActual(document.FilePath!), FileSystemPath.Comparer)
                .ToDictionary(group => group.Key, group => new Queue<TextDocument>(group), FileSystemPath.Comparer);
            var expectedDocuments = AllDocuments(project).Where(document => document.FilePath is not null)
                .GroupBy(document => DocumentKind(document) + "|" + mapPath(document.FilePath!), FileSystemPath.Comparer)
                .ToDictionary(group => group.Key, group => group.Count(), FileSystemPath.Comparer);
            if (!expectedDocuments.Keys.ToHashSet(FileSystemPath.Comparer).SetEquals(documents.Keys))
                throw new PhysicalGraphMismatchException("The evaluated typed document set differs from the reviewed graph. Missing: "
                    + string.Join(";", expectedDocuments.Keys.Except(documents.Keys)) + " Extra: " + string.Join(";", documents.Keys.Except(expectedDocuments.Keys)));
            if (expectedDocuments.Any(pair => documents[pair.Key].Count != pair.Value))
                throw new PhysicalGraphMismatchException("The evaluated typed document multiplicity differs from the reviewed graph.");
            string? generatedConfiguration = null;
            if (project.AnalyzerConfigDocuments.Any())
            {
                expectedGraph ??= EvaluatedBuildGraph.Source(expected, ImmutableDictionary<string, byte[]>.Empty, ct);
                var node = EvaluatedBuildGraph.Resolve(project, expectedGraph);
                generatedConfiguration = EvaluatedProjectGraph.GeneratedEditorConfigPath(node.ProjectPath, node.Globals);
            }
            foreach (var document in AllDocuments(project))
            {
                if (document.FilePath is null || !documents.TryGetValue(DocumentKind(document) + "|" + mapPath(document.FilePath), out var occurrences)
                    || !occurrences.TryDequeue(out var loadedDocument))
                    throw new PhysicalGraphMismatchException("The evaluated physical document set omits a reviewed document.");
                if (document is Document sourceDocument && loadedDocument is Document loadedSource
                    && sourceDocument.SourceCodeKind != loadedSource.SourceCodeKind)
                    throw new PhysicalGraphMismatchException("The evaluated source document kind differs from the reviewed snapshot.");
                var expectedText = await document.GetTextAsync(ct).ConfigureAwait(false);
                var loadedText = await loadedDocument.GetTextAsync(ct).ConfigureAwait(false);
                var generated = document.Project.GetAnalyzerConfigDocument(document.Id) is not null
                    && FileSystemPath.Comparer.Equals(document.FilePath, generatedConfiguration);
                var comparable = generated ? RelocateGeneratedConfig(expectedText.ToString(), mapPath, pathProperties) : expectedText.ToString();
                var actualComparable = generated ? RelocateGeneratedConfig(loadedText.ToString(), mapActual, pathProperties) : loadedText.ToString();
                if (!string.Equals(comparable, actualComparable, StringComparison.Ordinal))
                {
                    var failure = new PhysicalGraphMismatchException("The evaluated physical document text differs from the reviewed snapshot: "
                        + DocumentKind(document) + " " + document.FilePath);
                    failure.Data["SdkGeneratedConfig"] = generated;
                    failure.Data["SdkGeneratedConfigPath"] = generated ? document.FilePath : "<none>";
                    var left = comparable.Split('\n');
                    var right = actualComparable.Split('\n');
                    failure.Data["DifferentLineIndices"] = string.Join(",", Enumerable.Range(0, Math.Max(left.Length, right.Length))
                        .Where(index => index >= left.Length || index >= right.Length || left[index] != right[index]));
                    failure.Data["GeneratedConfigDifferenceKinds"] = string.Join(",", Enumerable.Range(0, Math.Max(left.Length, right.Length))
                        .Where(index => index >= left.Length || index >= right.Length || left[index] != right[index])
                        .Select(index => ConfigSlot(index < left.Length ? left[index] : right[index])).Distinct(StringComparer.Ordinal));
                    throw failure;
                }
            }
        }

        static string ConfigSlot(string line)
        {
            const string prefix = "build_property.";
            var separator = line.IndexOf(" = ", StringComparison.Ordinal);
            if (line.StartsWith(prefix, StringComparison.Ordinal) && separator >= prefix.Length)
            {
                var name = line[prefix.Length..separator];
                return BuildPathRelocation.IsKnownProperty(name) ? "PathProperty:" + name : "OtherBuildProperty";
            }
            return line.StartsWith('[') ? "SectionPath" : "OtherGeneratedConfigLine";
        }
    }

    internal static string RelocateGeneratedConfig(string text, Func<string, string> mapPath, IReadOnlySet<string> pathProperties)
    {
        // Only SDK-designated generated files reach this method. Preserve every non-path
        // value and every byte outside the rooted compiler-visible path/section value.
        var result = new StringBuilder();
        var offset = 0;
        while (offset < text.Length)
        {
            var end = text.IndexOf('\n', offset);
            if (end < 0) end = text.Length;
            var line = text[offset..end];
            var marker = line.IndexOf(" = ", StringComparison.Ordinal);
            if (line.StartsWith("build_property.", StringComparison.Ordinal) && marker >= 0
                && pathProperties.Contains(line["build_property.".Length..marker]))
            {
                var value = line[(marker + 3)..].TrimEnd('\r');
                if (Path.IsPathFullyQualified(value))
                    line = line[..(marker + 3)] + Map(value) + (line.EndsWith('\r') ? "\r" : "");
            }
            else if (line.StartsWith('[') && line.TrimEnd('\r').EndsWith(']'))
            {
                var value = line.TrimEnd('\r')[1..^1];
                if (Path.IsPathFullyQualified(value)) line = "[" + Map(value) + "]" + (line.EndsWith('\r') ? "\r" : "");
            }
            result.Append(line);
            if (end < text.Length) result.Append('\n');
            offset = end + 1;
        }
        return result.ToString();

        string Map(string path)
        {
            var mapped = mapPath(path);
            return Path.EndsInDirectorySeparator(path) && !Path.EndsInDirectorySeparator(mapped)
                ? mapped + path[^1] : mapped;
        }
    }

    private static string MetadataKey(MetadataReference reference, Func<string, string> mapPath)
        => MetadataIdentity(reference, mapPath).Key;

    private static (string Key, string FileName) MetadataIdentity(MetadataReference reference, Func<string, string> mapPath)
    {
        if (reference is not PortableExecutableReference { FilePath: { } path })
            throw new PublicInvalidOperationException("An assembly reference has no physical identity for graph verification.");
        var mapped = mapPath(path);
        var key = PhysicalKey(mapped) + "|" + reference.Properties.Kind
            + ReferenceMetadataKey(reference.Properties.Aliases, reference.Properties.EmbedInteropTypes, normalizeGlobal: false);
        return (key, Path.GetFileName(mapped));
    }

    private static string AnalyzerKey(Microsoft.CodeAnalysis.Diagnostics.AnalyzerReference reference, Func<string, string> mapPath) =>
        reference.FullPath is { } path ? PhysicalKey(mapPath(path))
            : throw new PublicInvalidOperationException("An analyzer reference has no physical identity for graph verification.");

    private static string PhysicalKey(string path) => OperatingSystem.IsWindows()
        ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);

    private static CompilationOptions? NormalizeOptions(CompilationOptions? options, Func<string, string> mapPath) =>
        options?.WithXmlReferenceResolver(null).WithSourceReferenceResolver(null).WithMetadataReferenceResolver(null)
            .WithStrongNameProvider(null).WithSyntaxTreeOptionsProvider(null)
            .WithCryptoKeyFile(string.IsNullOrEmpty(options.CryptoKeyFile) ? options.CryptoKeyFile : mapPath(options.CryptoKeyFile));

    private static Dictionary<string, Project> IndexProjects(Solution solution, Func<string, string> mapPath)
    {
        var result = new Dictionary<string, Project>(FileSystemPath.Comparer);
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is null || !result.TryAdd(PhysicalProjectIdentity.Key(project, mapPath), project))
                throw new PublicInvalidOperationException("The physical project identity is missing or ambiguous across evaluation contexts.");
        }
        return result;
    }

    private static string EvaluatedReferenceKey(EvaluatedProjectGraph.Reference reference)
    {
        var normalized = Path.GetFullPath(reference.TargetPath);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return normalized + ReferenceMetadataKey(reference.Aliases, reference.EmbedInteropTypes);
    }

    private static string ReferenceKey(Solution solution, ProjectReference reference, Func<string, string> mapPath)
    {
        var target = solution.GetProject(reference.ProjectId)
            ?? throw new PublicInvalidOperationException("A project reference target is missing.");
        var normalized = Path.GetFullPath(mapPath(target.FilePath
            ?? throw new PublicInvalidOperationException("A project reference target has no physical path.")));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return normalized + ReferenceMetadataKey(reference);
    }

    private static string ReferenceContextKey(Solution solution, ProjectReference reference, Func<string, string> mapPath)
    {
        var target = solution.GetProject(reference.ProjectId)
            ?? throw new PublicInvalidOperationException("A project reference target is missing.");
        return PhysicalProjectIdentity.Key(target, mapPath) + ReferenceMetadataKey(reference);
    }

    private static string ReferenceMetadataKey(ProjectReference reference) => ReferenceMetadataKey(reference.Aliases, reference.EmbedInteropTypes);

    internal static string ReferenceMetadataKey(IEnumerable<string> aliases, bool embed)
        => ReferenceMetadataKey(aliases, embed, normalizeGlobal: true);

    private static string ReferenceMetadataKey(IEnumerable<string> aliases, bool embed, bool normalizeGlobal)
    {
        var values = aliases.ToArray();
        var normalized = (normalizeGlobal && values.Length == 0 ? new[] { "global" } : values).OrderBy(alias => alias, StringComparer.Ordinal).ToArray();
        // Fixed JSON labels and exact framed string tokens contain no '|',
        // keeping this suffix distinct from the preceding physical/context key.
        return "|" + NativeStructuredIdentity.Serialize(new { Aliases = normalized, Embed = embed }, CancellationToken.None);
    }

    private static string SolutionProjectId(string path)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadSolutionProjectId(path);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string ReadSolutionProjectId(string path) =>
        new Microsoft.VisualStudio.SolutionPersistence.Model.SolutionModel().AddProject(path).Id.ToString("D");

}

internal interface IPhysicalSolutionCommitter
{
    Task CommitPhysicalSolutionAsync(string workspaceId, Solution? expected, CancellationToken ct);

}

internal sealed class PhysicalGraphMismatchException(string message) : InvalidOperationException(message);
