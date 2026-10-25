using System.Collections.Immutable;
using System.IO.Enumeration;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Execution;
using Microsoft.Build.FileSystem;
using Microsoft.Build.Graph;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using BuildProject = Microsoft.Build.Evaluation.Project;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Prepares ordinary preview effects and verifies their evaluated physical fork projection.</summary>
internal sealed class RestoreProjectGraph
{
    private sealed record DocumentEffect(string Path, byte[]? Bytes);
    private sealed record Node(string Path, ImmutableSortedDictionary<string, string> Properties,
        string[] References, string[] ReferenceItems, string[] CompileItems);

    private readonly IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> _projects;
    private readonly IReadOnlyList<DocumentEffect> _documents;
    private readonly Node[] _nodes;
    private readonly ImmutableArray<RestoreInvocationPlanner.Invocation> _invocations;

    private RestoreProjectGraph(IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> projects,
        IReadOnlyList<DocumentEffect> documents, Node[] nodes, ImmutableArray<RestoreInvocationPlanner.Invocation> invocations)
    {
        _projects = projects;
        _documents = documents;
        _nodes = nodes;
        _invocations = invocations;
    }

    internal static void ValidateSnapshot(Solution original, Solution modified, string sourceRoot)
    {
        var changes = modified.GetChanges(original);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any()) Unsupported();
        foreach (var change in changes.GetProjectChanges())
        {
            var oldProject = change.OldProject;
            var newProject = change.NewProject;
            RequirePath(oldProject.FilePath, sourceRoot);
            RequirePath(newProject.FilePath, sourceRoot);
            if (!FileSystemPath.Comparer.Equals(oldProject.FilePath, newProject.FilePath)
                || oldProject.Name != newProject.Name || oldProject.AssemblyName != newProject.AssemblyName
                || oldProject.OutputFilePath != newProject.OutputFilePath || oldProject.DefaultNamespace != newProject.DefaultNamespace
                || !Equals(oldProject.CompilationOptions, newProject.CompilationOptions)
                || !Equals(oldProject.ParseOptions, newProject.ParseOptions)
                || !oldProject.MetadataReferences.SequenceEqual(newProject.MetadataReferences)
                || !oldProject.AnalyzerReferences.SequenceEqual(newProject.AnalyzerReferences)
                || change.GetAddedAdditionalDocuments().Any() || change.GetRemovedAdditionalDocuments().Any()
                || change.GetChangedAdditionalDocuments().Any() || change.GetAddedAnalyzerConfigDocuments().Any()
                || change.GetRemovedAnalyzerConfigDocuments().Any() || change.GetChangedAnalyzerConfigDocuments().Any()) Unsupported();
            foreach (var id in change.GetChangedDocuments())
            {
                var before = oldProject.GetDocument(id);
                var after = newProject.GetDocument(id);
                if (before is null || after is null || before.Name != after.Name
                    || before.SourceCodeKind != after.SourceCodeKind || !before.Folders.SequenceEqual(after.Folders)
                    || !FileSystemPath.Comparer.Equals(before.FilePath, after.FilePath)) Unsupported();
            }
            foreach (var document in oldProject.Documents.Concat(newProject.Documents)) RequirePath(document.FilePath, sourceRoot);
        }

        static void Unsupported() => throw new PublicInvalidOperationException(
            "This preview contains project or document effects that cannot be represented by an ordinary workspace fork. Create a supported document/reference preview before retrying.");
    }

    internal static void ValidateCurrentBindings(Solution original, Solution modified, Solution current, string root)
    {
        foreach (var change in modified.GetChanges(original).GetProjectChanges())
        {
            var owner = BindProject(change.OldProject);
            foreach (var id in change.GetChangedDocuments().Concat(change.GetRemovedDocuments()))
            {
                var before = original.GetDocument(id) ?? throw BindingFailure();
                var direct = current.GetDocument(id);
                var matches = direct is null
                    ? current.Projects.SelectMany(project => project.Documents)
                        .Where(document => FileSystemPath.Comparer.Equals(document.FilePath, before.FilePath)).ToArray()
                    : [direct];
                if (matches.Length != 1 || matches[0].Project.Id != owner.Id
                    || !FileSystemPath.Comparer.Equals(matches[0].FilePath, before.FilePath)
                    || matches[0].Name != before.Name || !matches[0].Folders.SequenceEqual(before.Folders)
                    || matches[0].SourceCodeKind != before.SourceCodeKind) throw BindingFailure();
                RequirePath(matches[0].FilePath, root);
            }
            foreach (var reference in change.GetAddedProjectReferences())
                BindProject(modified.GetProject(reference.ProjectId) ?? throw BindingFailure());
            foreach (var reference in change.GetRemovedProjectReferences())
                BindProject(original.GetProject(reference.ProjectId) ?? throw BindingFailure());
        }

        Microsoft.CodeAnalysis.Project BindProject(Microsoft.CodeAnalysis.Project before)
        {
            var direct = current.GetProject(before.Id);
            var matches = direct is null
                ? current.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, before.FilePath)).ToArray()
                : [direct];
            if (matches.Length != 1 || !FileSystemPath.Comparer.Equals(matches[0].FilePath, before.FilePath)
                || matches[0].Name != before.Name || matches[0].AssemblyName != before.AssemblyName
                || !FileSystemPath.Comparer.Equals(matches[0].OutputFilePath, before.OutputFilePath)
                || !Equals(matches[0].ParseOptions, before.ParseOptions)) throw BindingFailure();
            RequirePath(matches[0].FilePath, root);
            return matches[0];
        }

        static PublicInvalidOperationException BindingFailure() => new(
            "A reviewed project or document target is missing, ambiguous, or has changed identity. Create a new preview before forking.");
    }

    internal static async Task<RestoreProjectGraph> PrepareAsync(string entryPath, string sourceRoot,
        Solution current, Solution intended, Func<TimeSpan> remaining, CancellationToken ct)
    {
        ValidateSnapshot(current, intended, sourceRoot);
        var projects = await ProjectReferenceMutationRenderer.PlanAsync(current, intended, ct).ConfigureAwait(false);
        var documents = new Dictionary<string, DocumentEffect>(FileSystemPath.Comparer);
        var membership = new Dictionary<string, (HashSet<string> Added, HashSet<string> Removed)>(FileSystemPath.Comparer);
        foreach (var change in intended.GetChanges(current).GetProjectChanges())
        {
            ct.ThrowIfCancellationRequested();
            remaining();
            var projectPath = RequirePath(change.NewProject.FilePath, sourceRoot);
            var added = change.GetAddedDocuments().Select(id => RequirePath(intended.GetDocument(id)?.FilePath, sourceRoot)).ToHashSet(FileSystemPath.Comparer);
            var removed = change.GetRemovedDocuments().Select(id => RequirePath(current.GetDocument(id)?.FilePath, sourceRoot)).ToHashSet(FileSystemPath.Comparer);
            if (membership.TryGetValue(projectPath, out var prior) && (!prior.Added.SetEquals(added) || !prior.Removed.SetEquals(removed)))
                throw new PublicInvalidOperationException("Compilation contexts require contradictory physical document membership in this fork.");
            membership[projectPath] = (added, removed);
            foreach (var id in change.GetAddedDocuments().Concat(change.GetChangedDocuments()))
            {
                var document = intended.GetDocument(id) ?? throw InvalidOperationErrors.Internal("A prepared fork document is missing.");
                var path = RequirePath(document.FilePath, sourceRoot);
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                var encoding = SourceFileEncoding.FromSourceText(text.Encoding);
                var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text.ToString())).ToArray();
                AddEffect(new DocumentEffect(path, bytes));
            }
            foreach (var path in removed) AddEffect(new DocumentEffect(path, null));
        }
        // A surviving physical compilation context owns its membership even when it has no text changes.
        foreach (var project in current.Projects)
        {
            if (project.FilePath is not { } path || !membership.TryGetValue(path, out var delta)) continue;
            var target = intended.GetProject(project.Id) ?? throw InvalidOperationErrors.Internal("A fork project disappeared after snapshot validation.");
            var currentPaths = project.Documents.Select(document => document.FilePath).OfType<string>().ToHashSet(FileSystemPath.Comparer);
            var targetPaths = target.Documents.Select(document => document.FilePath).OfType<string>().ToHashSet(FileSystemPath.Comparer);
            if (delta.Added.Any(item => !targetPaths.Contains(item)) || delta.Removed.Except(delta.Added, FileSystemPath.Comparer)
                .Any(item => currentPaths.Contains(item) && targetPaths.Contains(item)))
                throw new PublicInvalidOperationException("A physical document change is not requested by every compilation context of its project.");
        }

        foreach (var effect in documents.Values.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            remaining();
            var owners = intended.Projects.SelectMany(project => project.Documents)
                .Where(document => FileSystemPath.Comparer.Equals(document.FilePath, effect.Path)).ToArray();
            var wasRemoved = membership.Values.Any(delta => delta.Removed.Contains(effect.Path));
            var wasAdded = membership.Values.Any(delta => delta.Added.Contains(effect.Path));
            if (wasRemoved && owners.Length > 0 && !wasAdded)
                throw new PublicInvalidOperationException("A removed physical document still has a surviving compilation owner.");
            foreach (var owner in owners)
            {
                var text = await owner.GetTextAsync(ct).ConfigureAwait(false);
                var encoding = SourceFileEncoding.FromSourceText(text.Encoding);
                var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text.ToString())).ToArray();
                if (effect.Bytes is null || !effect.Bytes.AsSpan().SequenceEqual(bytes))
                    throw new PublicInvalidOperationException("Surviving compilation owners require contradictory physical document bytes or encoding.");
            }
        }

        var overrides = projects.ToDictionary(project => project.Path, project => project.Modified, FileSystemPath.Comparer);
        var physicalEffects = documents.Values.ToDictionary(effect => effect.Path, effect => effect.Bytes, FileSystemPath.Comparer);
        foreach (var project in projects) physicalEffects.Add(project.Path, project.Modified);
        var evaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared, new SnapshotFileSystem(physicalEffects, ct));
        var evaluated = new Dictionary<string, BuildProject>(StringComparer.Ordinal);
        return RestoreInvocationPlanner.EvaluateGraph(entryPath, remaining, ct, CreateInstance, (graph, token) =>
        {
            foreach (var instance in graph.ProjectNodes.Select(node => node.ProjectInstance))
            {
                token.ThrowIfCancellationRequested();
                remaining();
                if (!membership.TryGetValue(instance.FullPath, out var delta)) continue;
                // The framework outer is restore bookkeeping, not an owning compilation.
                if (instance.GetPropertyValue("TargetFrameworks").Length > 0 && instance.GetPropertyValue("TargetFramework").Length == 0) continue;
                var directory = Path.GetDirectoryName(instance.FullPath)!;
                var compilePaths = instance.GetItems("Compile").Select(item => ItemPath(item, directory)).ToHashSet(FileSystemPath.Comparer);
                if (!delta.Added.IsSubsetOf(compilePaths) || delta.Removed.Except(delta.Added, FileSystemPath.Comparer).Any(compilePaths.Contains))
                    throw new PublicInvalidOperationException("The requested document membership is not represented by the evaluated Compile inventory. Correct the project items before retrying.");
            }
            foreach (var instance in graph.ProjectNodes.Select(node => node.ProjectInstance))
                foreach (var property in instance.GlobalProperties)
                {
                    if (property.Key.Equals("CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase))
                    {
                        MapSolutionConfiguration(property.Value, static path => path);
                        continue;
                    }
                    if (property.Key.Equals("SolutionDir", StringComparison.OrdinalIgnoreCase)
                        || property.Key.Equals("SolutionPath", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ContainsOwnedPath(property.Value, sourceRoot))
                        throw new PublicInvalidOperationException("A source-owned reference global cannot be replayed by this physical fork.");
                }
            var nodes = CaptureNodes(graph, static path => path, remaining, token);
            foreach (var node in nodes) RequirePath(node.Path, sourceRoot);
            var invocations = RestoreInvocationPlanner.PlanInvocations(entryPath, graph, remaining, token);
            return new RestoreProjectGraph(projects, documents.Values.ToArray(), nodes, invocations);
        });

        void AddEffect(DocumentEffect effect)
        {
            if (documents.TryGetValue(effect.Path, out var prior))
            {
                if (prior.Bytes is not null && effect.Bytes is not null && !prior.Bytes.AsSpan().SequenceEqual(effect.Bytes))
                    throw new PublicInvalidOperationException("Compilation contexts require contradictory bytes for one physical fork document.");
                if (effect.Bytes is not null) documents[effect.Path] = effect;
            }
            else documents.Add(effect.Path, effect);
        }

        ProjectInstance CreateInstance(string path, Dictionary<string, string> properties, ProjectCollection collection)
        {
            ct.ThrowIfCancellationRequested();
            remaining();
            var key = ContextKey(path, properties);
            if (!evaluated.TryGetValue(key, out var project))
            {
                ProjectRootElement root;
                if (overrides.TryGetValue(path, out var bytes))
                {
                    using var input = new MemoryStream(bytes, writable: false);
                    using var reader = XmlReader.Create(input);
                    root = ProjectRootElement.Create(reader, collection);
                    root.FullPath = path;
                }
                else root = ProjectRootElement.Open(path, collection, preserveFormatting: true);
                project = BuildProject.FromProjectRootElement(root, new ProjectOptions
                {
                    GlobalProperties = properties,
                    ProjectCollection = collection,
                    LoadSettings = ProjectLoadSettings.RecordEvaluatedItemElements,
                    EvaluationContext = evaluationContext,
                });
                evaluated.Add(key, project);
            }
            return project.CreateProjectInstance();
        }
    }

    internal async Task<IReadOnlyList<string>> ReplayAsync(string sourceRoot, string forkRoot, CancellationToken ct)
    {
        var paths = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var project in _projects)
        {
            var path = MapPath(project.Path, sourceRoot, forkRoot);
            await File.WriteAllBytesAsync(path, project.Modified, ct).ConfigureAwait(false);
            paths.Add(path);
        }
        foreach (var document in _documents)
        {
            ct.ThrowIfCancellationRequested();
            var path = MapPath(document.Path, sourceRoot, forkRoot);
            if (document.Bytes is null) { if (File.Exists(path)) File.Delete(path); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, document.Bytes, ct).ConfigureAwait(false);
            }
            paths.Add(path);
        }
        return paths.Order(FileSystemPath.Comparer).ToArray();
    }

    internal string[] Verify(string entryPath, string sourceRoot, string forkRoot, Func<TimeSpan> remaining, CancellationToken ct)
    {
        var source = PhysicalPathResolver.Resolve(sourceRoot);
        var fork = PhysicalPathResolver.Resolve(forkRoot);
        return RestoreInvocationPlanner.EvaluateGraph(entryPath, remaining, ct, RestoreInvocationPlanner.CreateOrdinaryInstance, (graph, token) =>
        {
            foreach (var node in graph.ProjectNodes) ForkGraphPreparation.Inspect(node.ProjectInstance, source, fork, remaining, token);
            // Relocate actual fork identities back to their source-side intended identities for comparison.
            var actual = CaptureNodes(graph, path => MapPath(path, forkRoot, sourceRoot), remaining, token);
            var expected = _nodes.ToDictionary(node => ContextKey(node.Path, node.Properties), StringComparer.Ordinal);
            if (actual.Length != expected.Count) Mismatch("node count");
            foreach (var node in actual)
            {
                if (!expected.TryGetValue(ContextKey(node.Path, node.Properties), out var intended))
                {
                    var samePath = expected.Values.FirstOrDefault(value => FileSystemPath.Comparer.Equals(value.Path, node.Path));
                    var property = samePath is null ? null : node.Properties.Keys.Union(samePath.Properties.Keys, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(key => node.Properties.GetValueOrDefault(key) != samePath.Properties.GetValueOrDefault(key));
                    Mismatch(property is null ? "node identity" : "global property " + property);
                }
                if (!node.References.SequenceEqual(intended!.References)) Mismatch("reference edges");
                if (!node.ReferenceItems.SequenceEqual(intended.ReferenceItems)) Mismatch("reference items");
                if (!node.CompileItems.SequenceEqual(intended.CompileItems)) Mismatch("Compile items");
            }
            return graph.ProjectNodes.Select(node => node.ProjectInstance.FullPath).Distinct(FileSystemPath.Comparer).ToArray();
        });

        static void Mismatch(string category) => throw new PublicInvalidOperationException(
            "The copied evaluated project graph does not match the reviewed preview (" + category + "). Correct the project conditions and create a new preview before retrying.");
    }

    internal ImmutableArray<RestoreInvocationPlanner.Invocation> MapInvocations(string sourceRoot, string forkRoot)
    {
        return _invocations.Select(invocation =>
        {
            var target = MapPath(invocation.TargetPath, sourceRoot, forkRoot);
            var arguments = invocation.Arguments.ToArray();
            arguments[1] = target;
            for (var i = 2; i < arguments.Length; i++)
            {
                // Recorded package roots remain independent source-assets authority, not project relocation.
                if (arguments[i] == "--packages") { i++; continue; }
                if (!arguments[i].StartsWith("-p:", StringComparison.Ordinal)) continue;
                var separator = arguments[i].IndexOf('=');
                var value = ProjectCollection.Unescape(arguments[i][(separator + 1)..]);
                arguments[i] = arguments[i][..(separator + 1)] + ProjectCollection.Escape(
                    arguments[i][3..separator].Equals("CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase)
                        ? MapSolutionConfiguration(value, path => MapPath(path, sourceRoot, forkRoot))
                        : RelocateValue(value, sourceRoot, forkRoot));
            }
            return new RestoreInvocationPlanner.Invocation(target, arguments.ToImmutableArray());
        }).ToImmutableArray();
    }

    private static Node[] CaptureNodes(ProjectGraph graph, Func<string, string> projectPath,
        Func<TimeSpan> remaining, CancellationToken ct)
    {
        return graph.ProjectNodes.Select(node =>
        {
            ct.ThrowIfCancellationRequested();
            remaining();
            var instance = node.ProjectInstance;
            var path = projectPath(instance.FullPath);
            var directory = Path.GetDirectoryName(instance.FullPath)!;
            var properties = Properties(instance.GlobalProperties);
            var references = node.ProjectReferences.Select(reference => ContextKey(projectPath(reference.ProjectInstance.FullPath),
                Properties(reference.ProjectInstance.GlobalProperties))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new Node(path, properties, references, Items("ProjectReference"), Items("Compile"));

            string[] Items(string kind) => instance.GetItems(kind).Select(item =>
                projectPath(ItemPath(item, directory)) + "\n" + string.Join("\n", item.Metadata.OrderBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(metadata => metadata.Name.ToUpperInvariant() + "=" + Value(metadata.EvaluatedValue))))
                .Order(StringComparer.Ordinal).ToArray();

            ImmutableSortedDictionary<string, string> Properties(IDictionary<string, string> values) => values
                .ToImmutableSortedDictionary(property => property.Key, property => property.Key.Equals("CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase)
                    ? MapSolutionConfiguration(property.Value, projectPath) : Value(property.Value), StringComparer.OrdinalIgnoreCase);

            string Value(string value) => Path.IsPathRooted(value) ? projectPath(value) : value;
         }).GroupBy(node => ContextKey(node.Path, node.Properties), StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            if (group.Any(node => !node.ReferenceItems.SequenceEqual(first.ReferenceItems) || !node.CompileItems.SequenceEqual(first.CompileItems)))
                throw new PublicInvalidOperationException("Duplicate evaluated contexts have contradictory physical inventories.");
            return first with { References = group.SelectMany(node => node.References).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
        }).ToArray();
    }

    private static string ItemPath(ProjectItemInstance item, string directory) => Path.GetFullPath(item.EvaluatedInclude, directory);

    private static string ContextKey(string path, IEnumerable<KeyValuePair<string, string>> properties) =>
        (OperatingSystem.IsWindows() ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path)) + "\n" + string.Join("\n",
            properties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                .Select(property => property.Key.ToUpperInvariant() + ":" + property.Value.Length + ":" + property.Value));

    private static string RequirePath(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new PublicInvalidOperationException("A fork snapshot requires physical project and document paths.");
        var fullPath = Path.GetFullPath(path);
        if (!FileSystemPath.IsStrictDescendant(root, fullPath))
            throw new PublicInvalidOperationException("A fork snapshot contains a project or document outside its copied source root.");
        return fullPath;
    }

    private static string MapPath(string path, string sourceRoot, string forkRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var equalRoot = FileSystemPath.Comparer.Equals(source, full);
        if (!equalRoot && !FileSystemPath.IsStrictDescendant(source, full)) return path;
        var mapped = equalRoot ? Path.GetFullPath(forkRoot) : Path.GetFullPath(Path.Combine(forkRoot, Path.GetRelativePath(source, full)));
        return Path.EndsInDirectorySeparator(path) && !Path.EndsInDirectorySeparator(mapped)
            ? mapped + path[^1] : mapped;
    }

    private static bool ContainsOwnedPath(string value, string sourceRoot)
    {
        var normalized = ProjectCollection.Unescape(value).Replace('\\', '/');
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot)).Replace('\\', '/');
        var offset = 0;
        while ((offset = normalized.IndexOf(root, offset, FileSystemPath.Comparison)) >= 0)
        {
            var end = offset + root.Length;
            if (end == normalized.Length || "/;\"' \t\r\n<>".Contains(normalized[end])) return true;
            offset = end;
        }
        return false;
    }

    private static string MapSolutionConfiguration(string value, Func<string, string> map)
    {
        try
        {
            var document = XDocument.Parse(value, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != "SolutionConfiguration"
                || document.Root.Elements().Any(element => element.Name != "ProjectConfiguration"))
                throw new XmlException("Unsupported solution configuration shape.");
            foreach (var element in document.Root.Elements())
            {
                var path = element.Attribute("AbsolutePath");
                if (path is null || !Path.IsPathFullyQualified(path.Value))
                    throw new XmlException("A solution configuration project has no absolute path.");
                path.Value = map(path.Value);
            }
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException)
        {
            throw new PublicInvalidOperationException("The native solution configuration cannot be represented by this physical fork.");
        }
    }

    private static string RelocateValue(string value, string sourceRoot, string forkRoot) =>
        Path.IsPathRooted(value) ? MapPath(value, sourceRoot, forkRoot) : value;

    /// <summary>One evaluation's immutable physical effects; native MSBuild owns all item semantics.</summary>
    private sealed class SnapshotFileSystem : MSBuildFileSystemBase
    {
        private readonly Dictionary<string, byte[]?> _files;
        private readonly HashSet<string> _directories = new(FileSystemPath.Comparer);
        private readonly CancellationToken _ct;

        internal SnapshotFileSystem(IReadOnlyDictionary<string, byte[]?> files, CancellationToken ct)
        {
            _ct = ct;
            _files = new Dictionary<string, byte[]?>(FileSystemPath.Comparer);
            foreach (var (path, bytes) in files)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(path);
                _files.Add(fullPath, bytes?.ToArray());
                if (bytes is null) continue;
                for (var directory = Path.GetDirectoryName(fullPath); directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    _directories.Add(Path.TrimEndingDirectorySeparator(directory));
                }
            }
        }

        private bool Lookup(string path, out byte[]? bytes) => _files.TryGetValue(Path.GetFullPath(path), out bytes);
        private bool IsVirtualDirectory(string path) => _directories.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

        public override bool FileExists(string path)
        {
            _ct.ThrowIfCancellationRequested();
            return Lookup(path, out var bytes) ? bytes is not null : base.FileExists(path);
        }

        public override bool DirectoryExists(string path)
        {
            _ct.ThrowIfCancellationRequested();
            return IsVirtualDirectory(path) || base.DirectoryExists(path);
        }

        public override bool FileOrDirectoryExists(string path)
        {
            _ct.ThrowIfCancellationRequested();
            return Lookup(path, out var bytes) ? bytes is not null : IsVirtualDirectory(path) || base.FileOrDirectoryExists(path);
        }

        public override byte[] ReadFileAllBytes(string path)
        {
            _ct.ThrowIfCancellationRequested();
            return Lookup(path, out var bytes) ? RequireBytes(bytes).ToArray() : base.ReadFileAllBytes(path);
        }

        public override Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            _ct.ThrowIfCancellationRequested();
            if (mode != FileMode.Open || access != FileAccess.Read) throw new NotSupportedException("The evaluation snapshot is read-only.");
            return Lookup(path, out var bytes) ? new MemoryStream(RequireBytes(bytes).ToArray(), writable: false) : base.GetFileStream(path, mode, access, share);
        }

        public override TextReader ReadFile(string path)
        {
            _ct.ThrowIfCancellationRequested();
            return Lookup(path, out var bytes)
                ? new StreamReader(new MemoryStream(RequireBytes(bytes).ToArray(), writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true)
                : base.ReadFile(path);
        }

        public override string ReadFileAllText(string path)
        {
            _ct.ThrowIfCancellationRequested();
            if (!Lookup(path, out _)) return base.ReadFileAllText(path);
            using var reader = ReadFile(path);
            var text = reader.ReadToEnd();
            _ct.ThrowIfCancellationRequested();
            return text;
        }

        public override FileAttributes GetAttributes(string path)
        {
            _ct.ThrowIfCancellationRequested();
            if (Lookup(path, out var bytes)) { RequireBytes(bytes); return FileAttributes.Normal; }
            return IsVirtualDirectory(path) ? FileAttributes.Directory : base.GetAttributes(path);
        }

        public override DateTime GetLastWriteTimeUtc(string path)
        {
            _ct.ThrowIfCancellationRequested();
            if (Lookup(path, out var bytes)) { RequireBytes(bytes); return DateTime.UnixEpoch; }
            return IsVirtualDirectory(path) && !base.DirectoryExists(path) ? DateTime.UnixEpoch : base.GetLastWriteTimeUtc(path);
        }

        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            _ct.ThrowIfCancellationRequested();
            var entries = new HashSet<string>(FileSystemPath.Comparer);
            if (base.DirectoryExists(path))
                foreach (var file in base.EnumerateFiles(path, searchPattern, searchOption))
                {
                    _ct.ThrowIfCancellationRequested();
                    if (!Lookup(file, out var bytes) || bytes is not null) entries.Add(file);
                }
            foreach (var (file, bytes) in _files)
            {
                _ct.ThrowIfCancellationRequested();
                if (bytes is not null && Under(file, path, searchOption) && Matches(file, searchPattern)) entries.Add(file);
            }
            _ct.ThrowIfCancellationRequested();
            return entries.ToArray();
        }

        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            _ct.ThrowIfCancellationRequested();
            var entries = new HashSet<string>(FileSystemPath.Comparer);
            if (base.DirectoryExists(path))
                foreach (var directory in base.EnumerateDirectories(path, searchPattern, searchOption))
                {
                    _ct.ThrowIfCancellationRequested();
                    entries.Add(directory);
                }
            foreach (var directory in _directories)
            {
                _ct.ThrowIfCancellationRequested();
                if (Under(directory, path, searchOption) && Matches(directory, searchPattern)) entries.Add(directory);
            }
            _ct.ThrowIfCancellationRequested();
            return entries.ToArray();
        }

        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
        {
            _ct.ThrowIfCancellationRequested();
            var entries = EnumerateFiles(path, searchPattern, searchOption).Concat(EnumerateDirectories(path, searchPattern, searchOption)).ToArray();
            _ct.ThrowIfCancellationRequested();
            return entries;
        }

        private static byte[] RequireBytes(byte[]? bytes) => bytes ?? throw new FileNotFoundException("The file is absent from the evaluation snapshot.");

        private static bool Matches(string path, string pattern) => FileSystemName.MatchesWin32Expression(
            FileSystemName.TranslateWin32Expression(pattern), Path.GetFileName(path), OperatingSystem.IsWindows());

        private static bool Under(string candidate, string directory, SearchOption option)
        {
            var relative = Path.GetRelativePath(directory, candidate);
            return relative != "." && relative != ".." && !Path.IsPathRooted(relative)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && (option == SearchOption.AllDirectories || string.IsNullOrEmpty(Path.GetDirectoryName(relative)));
        }
    }
}
