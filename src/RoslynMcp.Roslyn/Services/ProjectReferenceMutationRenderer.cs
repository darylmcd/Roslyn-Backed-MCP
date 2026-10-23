using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Renders physical project edits from immutable original XML node bindings.</summary>
internal static class ProjectReferenceMutationRenderer
{
    internal sealed record Mutation(string Path, byte[] Original, byte[] Modified);
    private sealed record ReferenceIntent(string Target, string Aliases, bool EmbedInteropTypes);

    internal static async Task<IReadOnlyList<Mutation>> PlanAsync(
        Solution original, Solution modified, CancellationToken ct)
    {
        var snapshots = new Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot>(FileSystemPath.Comparer);
        foreach (var group in ChangedProjects(original, modified))
        {
            ct.ThrowIfCancellationRequested();
            snapshots.Add(group.Key, CsprojSemanticEquality.CreateSnapshot(
                await File.ReadAllBytesAsync(group.Key, ct).ConfigureAwait(false)));
        }
        return Plan(original, modified, snapshots, ct);
    }

    internal static IReadOnlyList<Mutation> Plan(
        Solution original, Solution modified,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> snapshots,
        CancellationToken ct)
    {
        var mutations = new List<Mutation>();
        foreach (var group in ChangedProjects(original, modified))
        {
            ct.ThrowIfCancellationRequested();
            if (!snapshots.TryGetValue(group.Key, out var snapshot))
                throw new IOException($"Could not snapshot project file '{group.Key}' before rendering.");
            var rendered = Render(snapshot, original, modified, group, ct);
            if (!snapshot.Bytes.AsSpan().SequenceEqual(rendered))
                mutations.Add(new Mutation(group.Key, snapshot.Bytes, rendered));
        }
        return mutations;
    }

    private static IEnumerable<IGrouping<string, ProjectChanges>> ChangedProjects(Solution original, Solution modified) =>
        modified.GetChanges(original).GetProjectChanges().Where(HasChanges)
            .GroupBy(change => change.NewProject.FilePath
                ?? throw new PublicInvalidOperationException("A reference-changing project requires an on-disk path."), FileSystemPath.Comparer)
            .OrderBy(group => group.Key, FileSystemPath.Comparer);

    internal static bool HasChanges(ProjectChanges change) =>
        change.GetAddedProjectReferences().Any() || change.GetRemovedProjectReferences().Any()
        || ((change.GetAddedDocuments().Any() || change.GetRemovedDocuments().Any())
            && change.NewProject.FilePath is { } path && File.Exists(path));

    private static byte[] Render(CsprojSemanticEquality.ProjectFileSnapshot snapshot,
        Solution original, Solution modified, IEnumerable<ProjectChanges> changes, CancellationToken ct)
    {
        var contexts = changes.ToArray();
        if (contexts.Length == 0)
            return snapshot.Bytes;
        var projectPath = contexts[0].NewProject.FilePath
            ?? throw InvalidOperationErrors.Internal("A reference-changing project requires an on-disk path.");
        var directory = Path.GetDirectoryName(projectPath)
            ?? throw InvalidOperationErrors.Internal("A project file requires a parent directory.");
        var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw InvalidOperationErrors.Internal("The project file has no root element.");
        var ns = root.Name.Namespace;
        var references = document.Descendants(ns + "ProjectReference").ToArray();
        var compileItems = document.Descendants(ns + "Compile").ToArray();
        var defaultCompile = ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(document);
        var referenceIntents = new Dictionary<XElement, ReferenceIntent?>();
        var additions = new List<ReferenceIntent>();
        var compileIntents = new Dictionary<XElement, bool>();
        var compileAdditions = new HashSet<string>(FileSystemPath.Comparer);
        var affectedReferenceTargets = contexts.SelectMany(context =>
                context.GetRemovedProjectReferences().Select(reference => Describe(reference, original).Target)
                    .Concat(context.GetAddedProjectReferences().Select(reference => Describe(reference, modified).Target)))
            .ToHashSet(FileSystemPath.Comparer);

        bool Targets(XElement item, string target) => DocumentSetPersistenceService.ProjectReferenceTargetsPath(
            (string?)item.Attribute("Include"), directory, target);

        void BindReference(XElement node, ReferenceIntent? intent)
        {
            if (referenceIntents.TryGetValue(node, out var previous) && !Equivalent(previous, intent))
                throw new PublicInvalidOperationException("Compilation contexts require contradictory edits to one physical project reference.");
            referenceIntents[node] = intent;
        }

        void BindCompile(XElement node, bool keep)
        {
            if (compileIntents.TryGetValue(node, out var previous) && previous != keep)
                throw new PublicInvalidOperationException("Compilation contexts require contradictory edits to one physical Compile item.");
            compileIntents[node] = keep;
        }

        // An unchanged sibling context still owns the original items it retains. Its
        // intent participates even when Roslyn omits that project from GetProjectChanges.
        foreach (var oldProject in original.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, projectPath)))
        {
            ct.ThrowIfCancellationRequested();
            var newProject = modified.GetProject(oldProject.Id);
            if (newProject is null || !FileSystemPath.Comparer.Equals(newProject.FilePath, projectPath))
                continue;
            var retainedReferences = newProject.ProjectReferences.Where(reference =>
                    modified.GetProject(reference.ProjectId)?.FilePath is { } target && affectedReferenceTargets.Contains(Path.GetFullPath(target)))
                .Select(reference => Describe(reference, modified)).ToArray();
            foreach (var reference in oldProject.ProjectReferences.Where(reference =>
                original.GetProject(reference.ProjectId)?.FilePath is { } target && affectedReferenceTargets.Contains(Path.GetFullPath(target))))
            {
                var intent = Describe(reference, original);
                if (!affectedReferenceTargets.Contains(intent.Target)
                    || !retainedReferences.Any(retained => Equivalent(intent, retained)))
                    continue;
                foreach (var node in references.Where(node => Targets(node, intent.Target) && MetadataMatches(node, intent)))
                    BindReference(node, intent);
            }
            if (defaultCompile)
                continue;
            var retainedPaths = newProject.Documents.Select(item => item.FilePath).OfType<string>()
                .ToHashSet(FileSystemPath.Comparer);
            foreach (var path in oldProject.Documents.Select(item => item.FilePath).OfType<string>().Where(retainedPaths.Contains))
                foreach (var node in compileItems.Where(node => Targets(node, path)))
                    BindCompile(node, true);
        }

        // No XML mutation occurs until every context has bound its operations to these original nodes.
        foreach (var context in contexts)
        {
            ct.ThrowIfCancellationRequested();
            var added = context.GetAddedProjectReferences()
                .Select(reference => (Reference: reference, Intent: Describe(reference, modified))).ToArray();
            var paired = new HashSet<ProjectReference>();
            foreach (var reference in context.GetRemovedProjectReferences())
            {
                var removed = Describe(reference, original);
                var nodes = references.Where(node => Targets(node, removed.Target) && MetadataMatches(node, removed)).ToArray();
                if (nodes.Length == 0)
                    throw new PublicInvalidOperationException("The removed project reference cannot be mapped to a concrete project-file item.");
                var candidates = added.Where(candidate => FileSystemPath.Comparer.Equals(candidate.Intent.Target, removed.Target)).ToArray();
                if (candidates.Select(candidate => (candidate.Intent.Aliases, candidate.Intent.EmbedInteropTypes)).Distinct().Count() > 1)
                {
                    var sameIdentity = candidates.Where(candidate => candidate.Reference.ProjectId == reference.ProjectId).ToArray();
                    if (sameIdentity.Length == 0)
                        throw new PublicInvalidOperationException("A project-reference replacement has ambiguous physical metadata intents.");
                    candidates = sameIdentity;
                }
                var replacements = candidates.Select(candidate => candidate.Intent)
                    .DistinctBy(intent => (intent.Aliases, intent.EmbedInteropTypes)).ToArray();
                if (replacements.Length > 1)
                    throw new PublicInvalidOperationException("A project-reference replacement has ambiguous physical metadata intents.");
                var replacement = replacements.SingleOrDefault();
                foreach (var candidate in candidates)
                    paired.Add(candidate.Reference);
                foreach (var node in nodes)
                    BindReference(node, replacement);
            }
            foreach (var candidate in added.Where(candidate => !paired.Contains(candidate.Reference)))
            {
                var nodes = references.Where(node => Targets(node, candidate.Intent.Target)
                    && MetadataMatches(node, candidate.Intent)).ToArray();
                if (nodes.Any(IsConditional)
                    && !context.OldProject.ProjectReferences.Any(reference => Equivalent(Describe(reference, original), candidate.Intent)))
                    throw new PublicInvalidOperationException("An added reference cannot be mapped to an existing conditional item in this compilation context.");
                foreach (var node in nodes)
                    BindReference(node, candidate.Intent);
                if (nodes.Length == 0 && !additions.Any(intent => Equivalent(intent, candidate.Intent)))
                    additions.Add(candidate.Intent);
            }

            if (defaultCompile)
                continue;
            var removedPaths = context.GetRemovedDocuments().Select(id => original.GetDocument(id)?.FilePath
                ?? throw new PublicInvalidOperationException("A removed document requires an on-disk path."))
                .ToHashSet(FileSystemPath.Comparer);
            var addedPaths = context.GetAddedDocuments().Select(id => modified.GetDocument(id)?.FilePath
                ?? throw new PublicInvalidOperationException("An added document requires an on-disk path."))
                .ToHashSet(FileSystemPath.Comparer);
            foreach (var path in removedPaths)
                foreach (var node in compileItems.Where(node => Targets(node, path)))
                    BindCompile(node, addedPaths.Contains(path));
            foreach (var path in addedPaths)
            {
                var nodes = compileItems.Where(node => Targets(node, path)).ToArray();
                if (nodes.Any(IsConditional)
                    && !context.OldProject.Documents.Any(item => FileSystemPath.Comparer.Equals(item.FilePath, path)))
                    throw new PublicInvalidOperationException("An added document cannot be mapped to an existing conditional Compile item in this compilation context.");
                foreach (var node in nodes)
                    BindCompile(node, true);
                if (nodes.Length == 0)
                    compileAdditions.Add(path);
            }
        }

        // A newly emitted unconditional item applies to every surviving context of
        // this physical project. Absence in a sibling is an ownership constraint too.
        foreach (var project in modified.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, projectPath)))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var intent in additions)
            {
                if (!project.ProjectReferences.Any(reference =>
                    modified.GetProject(reference.ProjectId)?.FilePath is { } target
                    && FileSystemPath.Comparer.Equals(Path.GetFullPath(target), intent.Target)
                    && Equivalent(Describe(reference, modified), intent)))
                    throw new PublicInvalidOperationException("A new unconditional project reference is not requested by every compilation context of the physical project.");
            }
            foreach (var path in compileAdditions)
                if (!project.Documents.Any(item => FileSystemPath.Comparer.Equals(item.FilePath, path)))
                    throw new PublicInvalidOperationException("A new unconditional Compile item is not requested by every compilation context of the physical project.");
        }

        var before = new XDocument(document);
        foreach (var (node, intent) in referenceIntents)
        {
            if (intent is null)
                node.Remove();
            else if (!MetadataMatches(node, intent))
                ApplyMetadata(node, ns, intent);
        }
        foreach (var (node, keep) in compileIntents)
            if (!keep)
                node.Remove();

        XElement ItemGroup()
        {
            var group = root.Elements(ns + "ItemGroup").FirstOrDefault(item => item.Attribute("Condition") is null);
            if (group is not null)
                return group;
            group = new XElement(ns + "ItemGroup");
            OrchestrationMsBuildXml.AppendRootChildWithFormatting(document, group);
            return group;
        }

        foreach (var intent in additions.OrderBy(intent => intent.Target, FileSystemPath.Comparer)
            .ThenBy(intent => intent.Aliases, StringComparer.Ordinal).ThenBy(intent => intent.EmbedInteropTypes))
        {
            var node = new XElement(ns + "ProjectReference", new XAttribute("Include", Path.GetRelativePath(directory, intent.Target)));
            ApplyMetadata(node, ns, intent);
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(ItemGroup(), node);
        }
        foreach (var path in compileAdditions.Order(FileSystemPath.Comparer))
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(ItemGroup(),
                new XElement(ns + "Compile", new XAttribute("Include", Path.GetRelativePath(directory, path))));
        return XNode.DeepEquals(before, document) ? snapshot.Bytes : snapshot.Encode(document);
    }

    private static ReferenceIntent Describe(ProjectReference reference, Solution solution) => new(
        Path.GetFullPath(solution.GetProject(reference.ProjectId)?.FilePath
            ?? throw new PublicInvalidOperationException("A project reference requires a physical target.")),
        string.Join(",", reference.Aliases.DefaultIfEmpty("global").Order(StringComparer.Ordinal)), reference.EmbedInteropTypes);

    private static bool Equivalent(ReferenceIntent? left, ReferenceIntent? right) =>
        left is null ? right is null : right is not null
            && FileSystemPath.Comparer.Equals(left.Target, right.Target)
            && left.Aliases == right.Aliases && left.EmbedInteropTypes == right.EmbedInteropTypes;

    private static string ReadMetadata(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value
        ?? element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value
        ?? string.Empty;

    private static bool IsConditional(XElement element) => element.AncestorsAndSelf()
        .Any(ancestor => ancestor.Attribute("Condition") is not null);

    private static bool MetadataMatches(XElement element, ReferenceIntent intent)
    {
        foreach (var name in new[] { "Aliases", "EmbedInteropTypes", "ReferenceOutputAssembly" })
        {
            var definitions = element.Attributes().Count(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                + element.Elements().Count(child => string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (definitions > 1)
                throw new PublicInvalidOperationException($"Multiple {name} metadata definitions cannot be bound to one compilation context safely.");
        }
        var aliases = string.Join(",", ReadMetadata(element, "Aliases")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).DefaultIfEmpty("global").Order(StringComparer.Ordinal));
        var outputAssembly = ReadMetadata(element, "ReferenceOutputAssembly");
        if (outputAssembly.Length > 0 && (!bool.TryParse(outputAssembly, out var output) || !output))
            return false;
        var embedText = ReadMetadata(element, "EmbedInteropTypes");
        var embed = false;
        return (embedText.Length == 0 || bool.TryParse(embedText, out embed))
            && aliases == intent.Aliases && embed == intent.EmbedInteropTypes;
    }

    private static void ApplyMetadata(XElement node, XNamespace ns, ReferenceIntent intent)
    {
        SetMetadata(node, ns + "Aliases", intent.Aliases, "global");
        SetMetadata(node, ns + "EmbedInteropTypes", intent.EmbedInteropTypes ? "true" : "false", "false");
    }

    private static void SetMetadata(XElement node, XName name, string value, string defaultValue)
    {
        var attributes = node.Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var elements = node.Elements().Where(child => string.Equals(child.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var attribute in attributes)
            attribute.Value = value;
        foreach (var element in elements)
        {
            // Preserve comments and metadata attributes rather than replacing the element.
            var textNodes = element.Nodes().OfType<XText>().ToArray();
            if (textNodes.Length == 0)
                element.AddFirst(new XText(value));
            else
            {
                textNodes[0].Value = value;
                foreach (var text in textNodes.Skip(1))
                    text.Remove();
            }
        }
        if (attributes.Length == 0 && elements.Length == 0 && value != defaultValue)
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(node, new XElement(name, value));
    }
}
