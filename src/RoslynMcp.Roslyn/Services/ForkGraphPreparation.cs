using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Checks that a copied, replayed graph represents the preview before restore executes.</summary>
internal static class ForkGraphPreparation
{
    private static readonly string[] OutputProperties =
    [
        "BaseIntermediateOutputPath", "IntermediateOutputPath", "MSBuildProjectExtensionsPath",
        "BaseOutputPath", "OutputPath", "OutDir", "TargetPath", "PublishDir",
        "RestoreOutputPath", "ProjectAssetsFile", "CompilerGeneratedFilesOutputPath",
        "GeneratedFilesOutputPath", "DocumentationFile", "ErrorLog",
    ];
    private static readonly HashSet<string> FileInputItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compile", "AdditionalFiles", "Analyzer", "AnalyzerConfigFiles", "EditorConfigFiles",
        "GlobalAnalyzerConfigFiles", "EmbeddedResource", "Resource", "Content", "None",
        "Reference", "ProjectReference", "NativeReference", "COMFileReference",
    };

    public static void ValidateSnapshotShape(Solution original, Solution modified, string sourceRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var changes = modified.GetChanges(original);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
            throw Unsupported("project additions or removals");

        foreach (var project in modified.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var before = original.GetProject(project.Id)
                ?? throw Unsupported("project additions");
            if (project.Name != before.Name || project.AssemblyName != before.AssemblyName
                || project.Language != before.Language || project.FilePath != before.FilePath
                || project.OutputFilePath != before.OutputFilePath
                || project.OutputRefFilePath != before.OutputRefFilePath
                || project.DefaultNamespace != before.DefaultNamespace
                || !Equals(project.CompilationOptions, before.CompilationOptions)
                || !Equals(project.ParseOptions, before.ParseOptions)
                || !project.MetadataReferences.SequenceEqual(before.MetadataReferences)
                || !project.AnalyzerReferences.SequenceEqual(before.AnalyzerReferences))
                throw Unsupported("project attributes, options, metadata references, or analyzers");

            RequireSourcePath(project.FilePath, sourceRoot);
            foreach (var reference in project.ProjectReferences)
            {
                ct.ThrowIfCancellationRequested();
                var target = modified.GetProject(reference.ProjectId)
                    ?? throw Unsupported("unresolved project references");
                RequireSourcePath(target.FilePath, sourceRoot);
            }

            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                var old = before.GetDocument(document.Id);
                if (old is null)
                    RequireSourcePath(document.FilePath, sourceRoot);
                if (old is not null && (old.FilePath != document.FilePath || old.Name != document.Name
                    || !old.Folders.SequenceEqual(document.Folders)
                    || old.SourceCodeKind != document.SourceCodeKind))
                    throw Unsupported("document attributes");
            }
        }

        foreach (var change in changes.GetProjectChanges())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var id in change.GetAddedDocuments().Concat(change.GetChangedDocuments()))
            {
                ct.ThrowIfCancellationRequested();
                RequireSourcePath(modified.GetDocument(id)?.FilePath, sourceRoot);
            }
            foreach (var id in change.GetRemovedDocuments())
            {
                ct.ThrowIfCancellationRequested();
                RequireSourcePath(original.GetDocument(id)?.FilePath, sourceRoot);
            }
            if (change.GetAddedAdditionalDocuments().Any() || change.GetRemovedAdditionalDocuments().Any()
                || change.GetChangedAdditionalDocuments().Any()
                || change.GetAddedAnalyzerConfigDocuments().Any() || change.GetRemovedAnalyzerConfigDocuments().Any()
                || change.GetChangedAnalyzerConfigDocuments().Any())
                throw Unsupported("additional or analyzer-config document changes");
        }

        foreach (var group in original.Projects.GroupBy(project => project.FilePath, FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            var profiles = group.Select(project => (
                Original: Profile(project, original, sourceRoot, sourceRoot, ct),
                OriginalDocuments: Documents(project, sourceRoot, sourceRoot, ct),
                Modified: Profile(modified.GetProject(project.Id)
                    ?? throw Unsupported("project removals"), modified, sourceRoot, sourceRoot, ct),
                ModifiedDocuments: Documents(modified.GetProject(project.Id)
                    ?? throw Unsupported("project removals"), sourceRoot, sourceRoot, ct))).ToArray();
            foreach (var first in profiles)
            foreach (var second in profiles)
            {
                ct.ThrowIfCancellationRequested();
                if (first.Original.SetEquals(second.Original)
                    && first.OriginalDocuments.SetEquals(second.OriginalDocuments)
                    && (!first.Modified.SetEquals(second.Modified)
                        || !first.ModifiedDocuments.SetEquals(second.ModifiedDocuments)))
                    throw Unsupported("ambiguous framework-specific project-reference changes");
            }
        }
    }

    public static void ValidateSnapshot(Solution original, Solution modified, string sourceRoot, CancellationToken ct)
    {
        ValidateSnapshotShape(original, modified, sourceRoot, ct);
        // Bind contexts while still before the copy, so ambiguity never creates a fork.
        MsBuildInitializer.EnsureInitialized();
        VerifyCore(original, modified, sourceRoot, sourceRoot, GlobalProperties(original), true, ct);
    }

    public static Task VerifyAsync(Solution originalSolution, Solution expectedSolution,
        string sourceRoot, string forkRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        VerifyCore(originalSolution, expectedSolution, sourceRoot, forkRoot, GlobalProperties(originalSolution), false, ct);
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyCore(Solution originalSolution, Solution expectedSolution, string sourceRoot, string forkRoot,
        Dictionary<string, string> globals, bool snapshotOnly, CancellationToken ct)
    {
        foreach (var group in expectedSolution.Projects.GroupBy(project =>
            MapPath(project.FilePath, sourceRoot, forkRoot, ct), FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            var profiles = group.Select(project => (
                Original: Profile(originalSolution.GetProject(project.Id)
                    ?? throw Unsupported("project additions"), originalSolution, sourceRoot, forkRoot, ct),
                OriginalDocuments: Documents(originalSolution.GetProject(project.Id)
                    ?? throw Unsupported("project additions"), sourceRoot, forkRoot, ct),
                Modified: Profile(project, expectedSolution, sourceRoot, forkRoot, ct),
                ModifiedDocuments: Documents(project, sourceRoot, forkRoot, ct))).ToArray();
            var sourcePath = originalSolution.GetProject(group.First().Id)?.FilePath
                ?? throw Unsupported("pathless projects");
            // Source evaluation binds Roslyn project profiles to concrete TFM contexts;
            // fork evaluation checks replay and isolation. This is not an external-writer lease.
            var sourceOuter = Evaluate(sourcePath, globals, sourceRoot, forkRoot, true, ct);
            var forkOuter = snapshotOnly ? sourceOuter : Evaluate(group.Key, globals, sourceRoot, forkRoot, false, ct);
            if (!sourceOuter.Frameworks.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(forkOuter.Frameworks))
                throw Unsupported("changed target-framework contexts");
            var frameworks = sourceOuter.Frameworks.Length == 0 ? [""] : sourceOuter.Frameworks;
            var sourceContexts = frameworks.Select(framework => framework.Length == 0 ? sourceOuter
                : Evaluate(sourcePath, new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase)
                    { ["TargetFramework"] = framework }, sourceRoot, forkRoot, true, ct)).ToArray();
            var compileUniverse = sourceContexts.SelectMany(context => context.Compiles).ToHashSet(FileSystemPath.Comparer);
            var documentUniverse = profiles.SelectMany(profile => profile.OriginalDocuments).ToHashSet(FileSystemPath.Comparer);
            var matched = new HashSet<int>();
            var contextIndex = 0;
            foreach (var framework in frameworks)
            {
                ct.ThrowIfCancellationRequested();
                var innerGlobals = new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase)
                    { ["TargetFramework"] = framework };
                var source = sourceContexts[contextIndex++];
                var fork = snapshotOnly || framework.Length == 0 ? forkOuter
                    : Evaluate(group.Key, innerGlobals, sourceRoot, forkRoot, false, ct);
                var matching = profiles.Select((profile, index) => (Profile: profile, Index: index)).Where(candidate =>
                    candidate.Profile.Original.SetEquals(source.References)
                    && candidate.Profile.OriginalDocuments.Intersect(compileUniverse, FileSystemPath.Comparer)
                        .ToHashSet(FileSystemPath.Comparer).SetEquals(source.Compiles.Intersect(documentUniverse, FileSystemPath.Comparer))).ToArray();
                if (matching.Length == 0)
                    throw Unsupported("an original snapshot that differs from the evaluated source graph");
                var intended = matching.Select(candidate =>
                {
                    matched.Add(candidate.Index);
                    var inputs = new HashSet<string>(source.Compiles, FileSystemPath.Comparer);
                    inputs.ExceptWith(candidate.Profile.OriginalDocuments.Except(candidate.Profile.ModifiedDocuments, FileSystemPath.Comparer));
                    inputs.UnionWith(candidate.Profile.ModifiedDocuments.Except(candidate.Profile.OriginalDocuments, FileSystemPath.Comparer));
                    return (candidate.Profile.Modified, Inputs: inputs);
                }).ToArray();
                if (intended.Any(profile => !profile.Modified.SetEquals(intended[0].Modified)
                    || !profile.Inputs.SetEquals(intended[0].Inputs)))
                    throw Unsupported("ambiguous framework-specific preview changes");
                if (!snapshotOnly && intended.Any(profile => !profile.Modified.SetEquals(fork.References)
                    || !profile.Inputs.SetEquals(fork.Compiles)
                    || !source.NonCompilationReferences.SetEquals(fork.NonCompilationReferences)))
                    throw new InvalidOperationException($"Copied project '{group.Key}' does not represent the preview project-reference graph for '{framework}'.");
            }
            if (matched.Count != profiles.Length)
                throw Unsupported("snapshot projects without an evaluated framework context");
            if (!snapshotOnly && forkOuter.References.Any(reference => !profiles.Any(profile => profile.Modified.Contains(reference))))
                throw new InvalidOperationException($"Copied project '{group.Key}' does not represent the preview project-reference graph.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evaluation Evaluate(string projectPath, Dictionary<string, string> globals,
        string sourceRoot, string forkRoot, bool sourceEvaluation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var collection = new ProjectCollection(globals);
        try
        {
            var project = collection.LoadProject(projectPath);
            ct.ThrowIfCancellationRequested();
            var references = new HashSet<ReferenceShape>();
            var nonCompilationReferences = new HashSet<ReferenceShape>();
            var compiles = new HashSet<string>(FileSystemPath.Comparer);
            var directory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("The copied project has no directory.");
            if (!sourceEvaluation)
            {
                foreach (var import in project.Imports)
                {
                    ct.ThrowIfCancellationRequested();
                    RejectSourcePath(import.ImportedProject.FullPath, directory, sourceRoot, forkRoot, "import");
                }
                foreach (var name in OutputProperties)
                {
                    ct.ThrowIfCancellationRequested();
                    RejectSourcePath(project.GetPropertyValue(name), directory, sourceRoot, forkRoot, "property " + name);
                }
            }

            foreach (var item in project.Items)
            {
                ct.ThrowIfCancellationRequested();
                // FullPath is MSBuild's reserved metadata and resolves relative item paths
                // against the evaluated project rather than this server's working directory.
                if (!sourceEvaluation)
                {
                    var fullPath = item.GetMetadataValue("FullPath");
                    // MSBuild gives every item FullPath metadata, including SDK bookkeeping
                    // such as SourceRoot and Folder. Those directory identities are not inputs.
                    // Known input kinds also cover absent files; existing custom file inputs
                    // are checked without treating arbitrary logical item identities as files.
                    if ((FileInputItemTypes.Contains(item.ItemType) && !IsAbsentSdkGlobalConfigProbe(item, project)) || File.Exists(fullPath))
                        RejectSourcePath(fullPath, directory, sourceRoot, forkRoot,
                            $"item {item.ItemType} (defined in {item.Xml.Location.File}; exists={File.Exists(fullPath)})");
                    RejectSourcePath(item.GetMetadataValue("HintPath"), directory, sourceRoot, forkRoot, "HintPath on " + item.ItemType);
                }
                if (item.ItemType.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
                {
                    var path = item.GetMetadataValue("FullPath");
                    if (string.IsNullOrWhiteSpace(path) || !FileSystemPath.IsStrictDescendant(
                        sourceEvaluation ? sourceRoot : forkRoot, path))
                        throw new InvalidOperationException("A copied project reference escapes the fork graph.");
                    if (sourceEvaluation)
                        path = MapPath(path, sourceRoot, forkRoot, ct);
                    var destination = string.Equals(item.GetMetadataValue("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)
                        ? nonCompilationReferences : references;
                    destination.Add(Shape(path, item.GetMetadataValue("Aliases").Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        string.Equals(item.GetMetadataValue("EmbedInteropTypes"), "true", StringComparison.OrdinalIgnoreCase)));
                }
                if (item.ItemType.Equals("Compile", StringComparison.OrdinalIgnoreCase))
                {
                    var path = item.GetMetadataValue("FullPath");
                    compiles.Add(sourceEvaluation && FileSystemPath.IsStrictDescendant(sourceRoot, path)
                        ? MapPath(path, sourceRoot, forkRoot, ct) : Path.GetFullPath(path));
                }
            }
            var frameworks = (project.GetPropertyValue("TargetFrameworks") + ";" + project.GetPropertyValue("TargetFramework"))
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new Evaluation(frameworks, references, nonCompilationReferences, compiles);
        }
        catch (InvalidProjectFileException ex)
        {
            throw new InvalidOperationException($"MSBuild could not verify copied project '{projectPath}': {ex.Message}", ex);
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    private static void RejectSourcePath(string value, string directory, string sourceRoot, string forkRoot, string origin)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var path = Path.GetFullPath(value, directory);
        if ((FileSystemPath.Comparer.Equals(path, Path.GetFullPath(sourceRoot))
            || FileSystemPath.IsStrictDescendant(sourceRoot, path))
            && !FileSystemPath.Comparer.Equals(path, Path.GetFullPath(forkRoot))
            && !FileSystemPath.IsStrictDescendant(forkRoot, path))
            throw new InvalidOperationException($"Copied project evaluation {origin} still refers to the source workspace: '{path}'.");
    }

    private static bool IsAbsentSdkGlobalConfigProbe(ProjectItem item, Microsoft.Build.Evaluation.Project project)
    {
        // The SDK expands all ancestor .globalconfig candidates into this item kind;
        // only candidates passing Exists() become EditorConfigFiles compiler inputs.
        // Explicit project declarations and existing configs retain ordinary input checks.
        if (!item.ItemType.Equals("GlobalAnalyzerConfigFiles", StringComparison.OrdinalIgnoreCase)
            || File.Exists(item.GetMetadataValue("FullPath"))
            || item.Xml.Include != "@(_AllDirectoriesAbove->'%(FullPath)'->Distinct()->Combine('.globalconfig'))")
            return false;
        var targetsPath = project.GetPropertyValue("RoslynTargetsPath");
        return Path.IsPathFullyQualified(targetsPath) && FileSystemPath.Comparer.Equals(
            Path.GetFullPath(item.Xml.Location.File), Path.GetFullPath(Path.Combine(targetsPath, "Microsoft.Managed.Core.targets")));
    }

    private static string MapPath(string? sourcePath, string sourceRoot, string forkRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RequireSourcePath(sourcePath, sourceRoot);
        return Path.GetFullPath(Path.Combine(forkRoot, Path.GetRelativePath(sourceRoot,
            sourcePath ?? throw Unsupported("pathless projects"))));
    }

    private static void RequireSourcePath(string? path, string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || !FileSystemPath.IsStrictDescendant(sourceRoot, path))
            throw Unsupported("paths outside the source workspace or missing on-disk paths");
    }

    private static InvalidOperationException Unsupported(string reason) =>
        new($"Fork apply cannot faithfully replay {reason}. Create a supported preview before copying the workspace.");

    private static ReferenceShape Shape(string path, IEnumerable<string> aliases, bool embedInteropTypes)
    {
        var normalized = aliases.DefaultIfEmpty("global").Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var fullPath = Path.GetFullPath(path);
        return new ReferenceShape(OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath,
            string.Join(",", normalized), embedInteropTypes);
    }

    private static HashSet<ReferenceShape> Profile(Microsoft.CodeAnalysis.Project project, Solution solution,
        string sourceRoot, string forkRoot, CancellationToken ct) => project.ProjectReferences.Select(reference =>
        Shape(MapPath(solution.GetProject(reference.ProjectId)?.FilePath, sourceRoot, forkRoot, ct),
            reference.Aliases, reference.EmbedInteropTypes)).ToHashSet();

    private static HashSet<string> Documents(Microsoft.CodeAnalysis.Project project, string sourceRoot, string forkRoot, CancellationToken ct) =>
        project.Documents.Where(document =>
        {
            ct.ThrowIfCancellationRequested();
            return document.FilePath is not null && FileSystemPath.IsStrictDescendant(sourceRoot, document.FilePath);
        }).Select(document => MapPath(document.FilePath, sourceRoot, forkRoot, ct)).ToHashSet(FileSystemPath.Comparer);

    private static Dictionary<string, string> GlobalProperties(Solution solution) =>
        solution.Workspace is MSBuildWorkspace workspace
            ? new Dictionary<string, string>(workspace.Properties, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed record Evaluation(string[] Frameworks, HashSet<ReferenceShape> References,
        HashSet<ReferenceShape> NonCompilationReferences, HashSet<string> Compiles);
    private sealed record ReferenceShape(string Path, string Aliases, bool EmbedInteropTypes);
}
