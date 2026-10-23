using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Renders project references and explicit Compile edits for preview, source persistence and fork replay.</summary>
internal static class ProjectReferenceMutationRenderer
{
    internal sealed record Mutation(string Path, byte[] Original, byte[] Modified);

    internal static async Task<IReadOnlyList<Mutation>> PlanAsync(
        Solution original, Solution modified, CancellationToken ct)
    {
        var mutations = new List<Mutation>();
        var groups = modified.GetChanges(original).GetProjectChanges().Where(HasChanges)
            .GroupBy(change => change.NewProject.FilePath
                ?? throw new InvalidOperationException("A reference-changing project requires an on-disk path."), FileSystemPath.Comparer);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var path = group.Key;
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var rendered = Render(bytes, original, modified, group);
            if (!bytes.AsSpan().SequenceEqual(rendered))
                mutations.Add(new Mutation(path, bytes, rendered));
        }
        return mutations;
    }

    internal static bool HasChanges(ProjectChanges change) =>
        change.GetAddedProjectReferences().Any() || change.GetRemovedProjectReferences().Any()
        || ((change.GetAddedDocuments().Any() || change.GetRemovedDocuments().Any())
            && change.NewProject.FilePath is { } path && File.Exists(path));

    internal static byte[] Render(byte[] bytes, Solution original, Solution modified, IEnumerable<ProjectChanges> changes)
    {
        foreach (var change in changes.DistinctBy(change => PhysicalIntent(change, original, modified)))
            bytes = RenderChange(bytes, original, modified, change);
        return bytes;
    }

    private static string PhysicalIntent(ProjectChanges change, Solution original, Solution modified)
    {
        static string Normalize(string? path) => path is null ? string.Empty
            : OperatingSystem.IsWindows() ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);
        static string Reference(ProjectReference reference, Solution solution) => System.Text.Json.JsonSerializer.Serialize(new
        {
            Path = Normalize(solution.GetProject(reference.ProjectId)?.FilePath),
            Aliases = reference.Aliases.DefaultIfEmpty("global").Order(StringComparer.Ordinal).ToArray(),
            reference.EmbedInteropTypes,
        });
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Removed = change.GetRemovedProjectReferences().Select(reference => Reference(reference, original)).Order(StringComparer.Ordinal).ToArray(),
            Added = change.GetAddedProjectReferences().Select(reference => Reference(reference, modified)).Order(StringComparer.Ordinal).ToArray(),
            RemovedDocuments = change.GetRemovedDocuments().Select(id => Normalize(original.GetDocument(id)?.FilePath)).Order(StringComparer.Ordinal).ToArray(),
            AddedDocuments = change.GetAddedDocuments().Select(id => Normalize(modified.GetDocument(id)?.FilePath)).Order(StringComparer.Ordinal).ToArray(),
        });
    }

    private static byte[] RenderChange(byte[] bytes, Solution original, Solution modified, ProjectChanges change)
    {
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);
        var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace);
        var originalDocument = new XDocument(document);
        var projectPath = change.NewProject.FilePath
            ?? throw new InvalidOperationException("A reference-changing project requires an on-disk path.");
        var directory = Path.GetDirectoryName(projectPath)!;
        var root = document.Root ?? throw new InvalidOperationException("The project file has no root element.");
        var ns = root.Name.Namespace;

        var added = change.GetAddedProjectReferences().ToArray();
        var replacementElements = new Dictionary<ProjectId, List<XElement>>();
        foreach (var reference in change.GetRemovedProjectReferences())
        {
            var target = original.GetProject(reference.ProjectId)?.FilePath
                ?? throw new InvalidOperationException("The removed project reference has no physical target.");
            var elements = document.Descendants(ns + "ProjectReference").Where(element =>
                DocumentSetPersistenceService.ProjectReferenceTargetsPath(
                    (string?)element.Attribute("Include"), directory, target)
                    && MetadataMatches(element, reference)).ToArray();
            if (elements.Length == 0)
                throw new InvalidOperationException("The removed project reference cannot be mapped to a concrete project-file item.");
            // A metadata replacement edits the existing XML node in place. Conditions,
            // unrelated metadata, comments and ordering are part of the physical reference.
            if (added.Any(candidate => candidate.ProjectId == reference.ProjectId))
                replacementElements[reference.ProjectId] = elements.ToList();
            else
                foreach (var element in elements)
                    element.Remove();
        }

        foreach (var reference in added)
        {
            var target = modified.GetProject(reference.ProjectId)?.FilePath
                ?? throw new InvalidOperationException("The added project reference has no physical target.");
            var elements = replacementElements.GetValueOrDefault(reference.ProjectId)
                ?? document.Descendants(ns + "ProjectReference").Where(candidate =>
                DocumentSetPersistenceService.ProjectReferenceTargetsPath(
                    (string?)candidate.Attribute("Include"), directory, target)
                    && MetadataMatches(candidate, reference)).ToList();
            if (elements.Count == 0)
            {
                var group = root.Elements(ns + "ItemGroup")
                    .FirstOrDefault(candidate => candidate.Attribute("Condition") is null);
                if (group is null)
                {
                    group = new XElement(ns + "ItemGroup");
                    OrchestrationMsBuildXml.AppendRootChildWithFormatting(document, group);
                }
                var element = new XElement(ns + "ProjectReference", new XAttribute("Include", Path.GetRelativePath(directory, target)));
                OrchestrationMsBuildXml.AddChildElementPreservingIndentation(group, element);
                elements.Add(element);
            }
            foreach (var element in elements)
            {
                SetMetadata(element, ns + "Aliases", reference.Aliases.Length == 0 ? null : string.Join(",", reference.Aliases));
                SetMetadata(element, ns + "EmbedInteropTypes", reference.EmbedInteropTypes ? "true" : null);
            }
        }
        if ((change.GetAddedDocuments().Any() || change.GetRemovedDocuments().Any())
            && !ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(projectPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance))
        {
            foreach (var id in change.GetRemovedDocuments())
            {
                var path = original.GetDocument(id)?.FilePath
                    ?? throw new InvalidOperationException("A removed document requires an on-disk path.");
                document.Descendants(ns + "Compile").Where(element =>
                    DocumentSetPersistenceService.ProjectReferenceTargetsPath(
                        (string?)element.Attribute("Include"), directory, path)).Remove();
            }
            foreach (var id in change.GetAddedDocuments())
            {
                var path = modified.GetDocument(id)?.FilePath
                    ?? throw new InvalidOperationException("An added document requires an on-disk path.");
                if (!document.Descendants(ns + "Compile").Any(element =>
                    DocumentSetPersistenceService.ProjectReferenceTargetsPath(
                        (string?)element.Attribute("Include"), directory, path)))
                {
                    var group = root.Elements(ns + "ItemGroup").FirstOrDefault(element => element.Attribute("Condition") is null);
                    if (group is null)
                    {
                        group = new XElement(ns + "ItemGroup");
                        OrchestrationMsBuildXml.AppendRootChildWithFormatting(document, group);
                    }
                    OrchestrationMsBuildXml.AddChildElementPreservingIndentation(group,
                        new XElement(ns + "Compile", new XAttribute("Include", Path.GetRelativePath(directory, path))));
                }
            }
        }
        return XNode.DeepEquals(originalDocument, document) ? bytes : snapshot.Encode(document);
    }

    private static void SetMetadata(XElement element, XName name, string? value)
    {
        element.Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase)).Remove();
        element.Elements().Where(child => string.Equals(child.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase)).Remove();
        if (value is not null)
            element.Add(new XElement(name, value));
    }

    private static bool MetadataMatches(XElement element, ProjectReference reference)
    {
        string Read(string name) => element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? string.Empty;
        var aliases = Read("Aliases").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("global").Order(StringComparer.Ordinal);
        var expectedAliases = reference.Aliases.DefaultIfEmpty("global").Order(StringComparer.Ordinal);
        var embedText = Read("EmbedInteropTypes");
        if (string.Equals(Read("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
            return false;
        var embed = false;
        if (embedText.Length > 0 && !bool.TryParse(embedText, out embed))
            return false;
        return aliases.SequenceEqual(expectedAliases) && embed == reference.EmbedInteropTypes;
    }
}
