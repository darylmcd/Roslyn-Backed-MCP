using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Maps the loaded authored graph without recursively copying unrelated common ancestors.</summary>
internal sealed record PhysicalGraphLayout(string Root, string LoadedDirectory, IReadOnlyList<string> ContentRoots,
    IReadOnlyList<string> DefinitionFiles, IReadOnlyList<string> ArtifactFiles, ImmutablePathProvenance Provenance,
    IReadOnlyList<string> OutputDirectories, IReadOnlyList<string> DocumentFiles, IReadOnlyList<string> InputFiles, IReadOnlyList<EvaluatedBuildGraph.Node> BuildNodes, ImmutableArray<PhysicalInputIdentity> ConfigurationInputs, IReadOnlyList<string> MappingDirectories, ImmutableArray<PhysicalInputIdentity> ConditionInputs, InstalledPackageAuthority Packages,
    ImmutableArray<SdkExistenceInput> SdkInputs, IReadOnlyList<string> ContentReplayFiles,
    NativeBuildMembership? CapturedSourceMembership)
{
    private PhysicalPathMembership _inputMembership = new(InputFiles);
    private PhysicalPathMembership _replayMembership = new(ContentReplayFiles);
    public IReadOnlyList<string> InputFiles { get => _inputMembership.Files; init => _inputMembership = new(value); }
    public IReadOnlyList<string> ContentReplayFiles { get => _replayMembership.Files; init => _replayMembership = new(value); }
    internal ImmutableArray<string> ImmutableRoots => Provenance.DirectoryRoots;
    internal ImmutableArray<string> AuthoredRoots => Provenance.AuthoredRoots;
    // Union/WithAdditionalInputs retain this source-capture epoch; it does not
    // describe every node subsequently added to the copied inventory.
    internal NativeBuildMembership RequireCapturedSourceMembership() => CapturedSourceMembership
        ?? throw InvalidOperationErrors.Internal("This copy-only layout has no captured native source membership.");

    internal static async Task<PhysicalGraphLayout> CreateAsync(string workspaceId, string loadedPath, Solution source,
        IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        await pathPolicy.ValidateAsync(workspaceId, source.Projects.Select(project => project.FilePath).OfType<string>()
            .Append(loadedPath).Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
        loadedPath = Path.GetFullPath(loadedPath);
        WorkspaceManager.EnsurePhysicalPath(loadedPath);
        var loadedDirectory = Path.GetDirectoryName(loadedPath)
            ?? throw new PublicInvalidOperationException("The loaded graph has no physical directory.");
        var graph = EvaluatedBuildGraph.Source(source, ImmutableDictionary<string, byte[]>.Empty, ct);
        var capturedMembership = NativeBuildMembership.Capture(graph, ct);
        var inputs = graph.Nodes.Select(EvaluatedProjectGraph.DefinitionInputs).ToArray();
        var assets = graph.Nodes.Select(node => node.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
        await pathPolicy.ValidateAsync(workspaceId, assets, ct).ConfigureAwait(false);
        var packageProof = await BuildInputProvenance.ReadPackagesAsync(assets, graph.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(), (paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        var packageRoots = packageProof.Files;
        var immutableDirectories = inputs.SelectMany(input => input.ImmutableRoots);
        var authoredRoots = graph.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!)
            .Concat(source.Projects.Select(project => project.FilePath).OfType<string>().Select(path => Path.GetDirectoryName(path)!))
            .Distinct(FileSystemPath.Comparer).ToArray();
        var provenance = new ImmutablePathProvenance(immutableDirectories, packageRoots, authoredRoots);
        bool IsImmutable(string path) => provenance.IsImmutable(path);
        var sdkInputs = SdkExistenceInput.Capture(graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes)
            .Where(SdkExistenceInput.Supports), ct);
        var conditionProbes = graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes
            .Where(probe => !SdkExistenceInput.Supports(probe) && !IsImmutable(probe.Path) && (!IsImmutable(probe.Origin)
                || BuildPathRoles.IsNativeInputPath(node.PathEvidence, probe.Path)))).ToArray();
        var conditionPaths = conditionProbes.Select(probe => probe.Path).Distinct(FileSystemPath.Comparer).ToArray();
        await pathPolicy.ValidateAsync(workspaceId, conditionPaths, ct).ConfigureAwait(false);
        var conditionBuilder = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var path in conditionPaths)
        {
            await pathPolicy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
            conditionBuilder.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        }
        var conditions = conditionBuilder.ToImmutable();
        ConditionInputTruth.AssertObserved(conditionProbes, conditions);
        var configuration = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var directory in authoredRoots.Append(loadedDirectory).Distinct(FileSystemPath.Comparer))
        {
            var context = await DotnetTestModeResolver.ResolveAsync(workspaceId, directory, pathPolicy, ct).ConfigureAwait(false);
            configuration.AddRange(context.Candidates.Select(candidate => new PhysicalInputIdentity(candidate.FilePath, candidate.Exists, candidate.ContentHash)));
        }
        var configurationInputs = MergeConfigurationInputs(configuration);
        var definitions = inputs.SelectMany(input => input.Definitions).Where(path => !IsImmutable(path))
            .Append(loadedPath).Concat(configurationInputs.Where(input => input.Exists).Select(input => input.FilePath))
            .Distinct(FileSystemPath.Comparer).ToArray();
        var directories = graph.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!)
            .Concat(source.Projects.Select(project => project.FilePath).OfType<string>().Select(path => Path.GetDirectoryName(path)!))
            .Append(loadedDirectory).Where(path => !IsImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
        // A package file can also be a native project item (for example a None
        // candidate searched by RAR). Preserve that item in the private layout,
        // without turning its package origin into an authored build definition.
        var contentReplayFiles = ContentReplayInputs(graph.Nodes, directories, provenance);
        var authoredDocuments = source.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments)
            .Select(document => document.FilePath).OfType<string>().Where(path => !IsImmutable(path)).ToArray();
        var artifacts = source.Projects.SelectMany(project => project.MetadataReferences).OfType<PortableExecutableReference>()
            .Select(reference => reference.FilePath).OfType<string>()
            .Concat(source.Projects.SelectMany(project => project.AnalyzerReferences).Select(reference => reference.FullPath).OfType<string>())
            .Concat(source.Projects.SelectMany(project => project.CompilationOptions?.CryptoKeyFile is { Length: > 0 } key
                && project.FilePath is { } projectPath ? new[] { Path.GetFullPath(key, Path.GetDirectoryName(projectPath)!) } : []))
            .Concat(graph.Nodes.SelectMany(node => node.PathEvidence.InputFiles))
            .Where(path => !IsImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
        var mappingDirectories = graph.Nodes.SelectMany(node => node.PathEvidence.InputCoordinates)
            .Where(coordinate => !IsImmutable(coordinate.Origin) && !IsImmutable(coordinate.Target)).Select(coordinate => coordinate.Directory)
            .Concat(graph.Nodes.SelectMany(node => node.PathEvidence.NativeInputDirectories).Where(path => !IsImmutable(path)))
            .Concat(graph.Nodes.SelectMany(node => node.PathEvidence.OutputCoordinates)).Distinct(FileSystemPath.Comparer).ToArray();
        var outputs = graph.Nodes.Select(node => node.TargetPath).Concat(source.Projects.Select(project => project.OutputFilePath))
            .OfType<string>().Select(path => Path.GetDirectoryName(path)!)
            .Concat(graph.Nodes.Select(node => node.Inputs.ExtensionsPath).OfType<string>()).Concat(graph.Nodes.SelectMany(node => node.PathEvidence.Directories))
            .Select(Path.TrimEndingDirectorySeparator).Distinct(FileSystemPath.Comparer).ToArray();
        var restoreFiles = graph.Nodes.SelectMany(node => new[] { node.Inputs.AssetsPath,
                node.Inputs.ExtensionsPath is { } extension ? Path.Combine(extension, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json") : null })
            .OfType<string>().Where(path => !IsImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
        var root = loadedDirectory;
        var paths = definitions.Concat(authoredDocuments).Concat(artifacts).Concat(graph.Nodes.Select(node => node.ProjectPath))
            .Concat(source.Projects.Select(project => project.FilePath).OfType<string>()).Concat(outputs).Concat(mappingDirectories).Concat(conditions.Select(input => input.FilePath)).Concat(restoreFiles).ToArray();
        foreach (var path in paths)
            while (!Contains(root, path))
                root = Path.GetDirectoryName(root)
                    ?? throw new PublicInvalidOperationException("The authored projects and inputs do not share one physical graph volume.");
        if (directories.Any(directory => FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(directory)!))))
            throw new PublicInvalidOperationException("The loaded graph requires recursively copying a volume root. Move the project into a bounded directory.");
        await pathPolicy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false);
        foreach (var path in paths)
        {
            WorkspaceManager.EnsurePhysicalPath(path);
            if (!Contains(root, path))
                throw new PublicInvalidOperationException("An authored graph input is outside the loaded solution boundary.");
        }
        var contentRoots = directories.Where(directory => !directories.Any(other => !FileSystemPath.Comparer.Equals(other, directory)
                && FileSystemPath.IsStrictDescendant(other, directory))).ToArray();
        SdkExistenceInput.AssertCurrent(sdkInputs, ct);
        return new PhysicalGraphLayout(root, loadedDirectory, contentRoots, definitions, artifacts, provenance, outputs, authoredDocuments,
            authoredDocuments.Concat(definitions).Concat(artifacts).Concat(restoreFiles).Concat(contentReplayFiles).Concat(conditions.Where(input => input.Kind != PhysicalInputKind.Directory).Select(input => input.FilePath)).Distinct(FileSystemPath.Comparer).ToArray(), graph.Nodes, configurationInputs, mappingDirectories, conditions, packageProof, sdkInputs, contentReplayFiles, capturedMembership);
    }

    internal static PhysicalGraphLayout Union(PhysicalGraphLayout current, PhysicalGraphLayout prior)
    {
        var root = current.Root;
        foreach (var path in prior.ContentRoots.Concat(prior.InputFiles).Concat(prior.OutputDirectories).Concat(prior.MappingDirectories).Concat(prior.ConditionInputs.Select(input => input.FilePath)))
            while (!Contains(root, path)) root = Path.GetDirectoryName(root)
                ?? throw new PublicInvalidOperationException("The recovery graph spans multiple physical volumes.");
        var directories = current.ContentRoots.Concat(prior.ContentRoots).Distinct(FileSystemPath.Comparer).ToArray();
        directories = directories.Where(directory => !directories.Any(other => !FileSystemPath.Comparer.Equals(other, directory)
            && FileSystemPath.IsStrictDescendant(other, directory))).ToArray();
        return new PhysicalGraphLayout(root, current.LoadedDirectory, directories,
            Merge(current.DefinitionFiles, prior.DefinitionFiles), Merge(current.ArtifactFiles, prior.ArtifactFiles),
            new ImmutablePathProvenance(current.ImmutableRoots.Concat(prior.ImmutableRoots), current.Provenance.ExactFiles.Concat(prior.Provenance.ExactFiles), current.AuthoredRoots.Concat(prior.AuthoredRoots)), Merge(current.OutputDirectories, prior.OutputDirectories),
            Merge(current.DocumentFiles, prior.DocumentFiles), Merge(current.InputFiles, prior.InputFiles), current.BuildNodes.Concat(prior.BuildNodes).Distinct().ToArray(), OverlayConfigurationInputs(prior.ConfigurationInputs, current.ConfigurationInputs), Merge(current.MappingDirectories, prior.MappingDirectories), OverlayConfigurationInputs(prior.ConditionInputs, current.ConditionInputs), current.Packages.RetainPackageFilesFrom(prior.Packages), current.SdkInputs, Merge(current.ContentReplayFiles, prior.ContentReplayFiles), current.CapturedSourceMembership);

        static string[] Merge(IEnumerable<string> first, IEnumerable<string> second) => first.Concat(second).Distinct(FileSystemPath.Comparer).ToArray();
    }

    internal static PhysicalGraphLayout WithAdditionalInputs(PhysicalGraphLayout current,
        IReadOnlyList<string> newProjectDirectories, IReadOnlyList<string> newDefinitionFiles,
        IReadOnlyList<string> newArtifactFiles, IReadOnlyList<EvaluatedBuildGraph.Node> desiredNodes, InstalledPackageAuthority packages,
        IReadOnlyList<PhysicalInputIdentity> configurationInputs, IReadOnlyList<PhysicalInputIdentity> conditionInputs,
        IReadOnlyList<SdkExistenceInput> sdkInputs)
    {
        static string[] Merge(IEnumerable<string> first, IEnumerable<string> second) => first.Concat(second).Distinct(FileSystemPath.Comparer).ToArray();
        packages = packages.RetainPackageFilesFrom(current.Packages);
        var immutableDirectories = current.ImmutableRoots.Concat(desiredNodes.SelectMany(node => node.BuildInputs.ImmutableRoots));
        var authoredRoots = Merge(current.AuthoredRoots, newProjectDirectories);
        var provenance = new ImmutablePathProvenance(immutableDirectories, current.Provenance.ExactFiles.Concat(packages.Files), authoredRoots);
        bool IsImmutable(string path) => (!current._inputMembership.Paths.Contains(path) || current.IsContentReplayFile(path))
            && provenance.IsImmutable(path);
        var mergedConfiguration = OverlayConfigurationInputs(current.ConfigurationInputs, configurationInputs);
        var mergedConditions = OverlayConfigurationInputs(current.ConditionInputs, conditionInputs);
        var definitions = Merge(current.DefinitionFiles, newDefinitionFiles.Concat(mergedConfiguration.Where(input => input.Exists).Select(input => input.FilePath))).Where(path => !IsImmutable(path)).ToArray();
        var artifacts = Merge(current.ArtifactFiles, newArtifactFiles.Concat(desiredNodes.SelectMany(node => node.PathEvidence.InputFiles))).Where(path => !IsImmutable(path)).ToArray();
        var directories = Merge(current.ContentRoots, newProjectDirectories).Where(path => !IsImmutable(path)).ToArray();
        if (directories.Any(directory => FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(directory)!))))
            throw new PublicInvalidOperationException("A newly active recovery project requires a volume-root copy.");
        directories = directories.Where(directory => !directories.Any(other => !FileSystemPath.Comparer.Equals(other, directory)
            && FileSystemPath.IsStrictDescendant(other, directory))).ToArray();
        var contentReplayFiles = Merge(current.ContentReplayFiles, ContentReplayInputs(desiredNodes, directories, provenance));
        var mappingDirectories = Merge(current.MappingDirectories, desiredNodes.SelectMany(node => node.PathEvidence.InputCoordinates)
            .Where(coordinate => !IsImmutable(coordinate.Origin) && !IsImmutable(coordinate.Target)).Select(coordinate => coordinate.Directory)
            .Concat(desiredNodes.SelectMany(node => node.PathEvidence.NativeInputDirectories).Where(path => !IsImmutable(path)))
            .Concat(desiredNodes.SelectMany(node => node.PathEvidence.OutputCoordinates)));
        var outputs = Merge(current.OutputDirectories, desiredNodes.Select(node => node.TargetPath).OfType<string>()
            .Select(path => Path.GetDirectoryName(path)!).Concat(desiredNodes.Select(node => node.Inputs.ExtensionsPath).OfType<string>()).Concat(desiredNodes.SelectMany(node => node.PathEvidence.Directories)).Select(Path.TrimEndingDirectorySeparator));
        var restoreFiles = desiredNodes.SelectMany(node => new[] { node.Inputs.AssetsPath,
            node.Inputs.ExtensionsPath is { } extension ? Path.Combine(extension, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json") : null }).OfType<string>();
        var inputs = Merge(current.InputFiles, definitions.Concat(artifacts).Concat(restoreFiles).Concat(contentReplayFiles).Concat(mergedConditions.Where(input => input.Kind != PhysicalInputKind.Directory).Select(input => input.FilePath)));
        var root = current.Root;
        foreach (var path in inputs.Concat(directories).Concat(outputs).Concat(mappingDirectories).Concat(mergedConditions.Select(input => input.FilePath)))
        {
            WorkspaceManager.EnsurePhysicalPath(path);
            while (!Contains(root, path)) root = Path.GetDirectoryName(root)
                ?? throw new PublicInvalidOperationException("The newly active recovery graph spans multiple physical volumes.");
        }
        return current with
        {
            Root = root,
            ContentRoots = directories,
            DefinitionFiles = definitions,
            ArtifactFiles = artifacts,
            OutputDirectories = outputs,
            InputFiles = inputs,
            Provenance = provenance,
            MappingDirectories = mappingDirectories,
            BuildNodes = current.BuildNodes.Concat(desiredNodes).Distinct().ToArray(),
            ConfigurationInputs = mergedConfiguration,
            ConditionInputs = mergedConditions,
            Packages = packages,
            SdkInputs = current.SdkInputs.Concat(sdkInputs).Distinct().ToImmutableArray(),
            ContentReplayFiles = contentReplayFiles
        };
    }

    private static ImmutableArray<PhysicalInputIdentity> OverlayConfigurationInputs(
        IEnumerable<PhysicalInputIdentity> historical, IEnumerable<PhysicalInputIdentity> current)
    {
        var result = historical.ToDictionary(input => input.FilePath, FileSystemPath.Comparer);
        foreach (var input in MergeConfigurationInputs(current)) result[input.FilePath] = input;
        return result.Values.ToImmutableArray();
    }

    private static ImmutableArray<PhysicalInputIdentity> MergeConfigurationInputs(IEnumerable<PhysicalInputIdentity> inputs) =>
        PhysicalInputIdentity.MergeConsistent(inputs);

    internal bool IsImmutablePath(string path) => (!_inputMembership.Paths.Contains(path) || IsContentReplayFile(path))
        && Provenance.IsImmutable(path);

    internal bool IsContentReplayFile(string path) => _replayMembership.Paths.Contains(path);

    private static string[] ContentReplayInputs(IEnumerable<EvaluatedBuildGraph.Node> nodes,
        IReadOnlyList<string> contentRoots, ImmutablePathProvenance provenance)
        => nodes.SelectMany(node => node.PathEvidence.InputFiles)
            .Where(path => provenance.IsImmutable(path) && contentRoots.Any(root => Contains(root, path)))
            .Distinct(FileSystemPath.Comparer).ToArray();

    internal string MapDefinitionPath(string destination, string sourcePath)
        => IsImmutablePath(sourcePath) ? sourcePath : MapInputPath(destination, sourcePath);

    internal string MapInputPath(string destination, string sourcePath)
    {
        if (Contains(destination, sourcePath) || IsImmutablePath(sourcePath) && !IsContentReplayFile(sourcePath)) return sourcePath;
        var owned = ContentRoots.Concat(OutputDirectories).Concat(MappingDirectories).Concat(ConditionInputs.Where(input => input.Kind == PhysicalInputKind.Directory).Select(input => input.FilePath)).Any(root => Contains(root, sourcePath))
            || _inputMembership.Paths.Contains(sourcePath)
            || _inputMembership.Parents.Contains(Path.TrimEndingDirectorySeparator(sourcePath));
        if (!owned) return sourcePath;
        var mapped = MapTo(destination, sourcePath);
        return Path.EndsInDirectorySeparator(sourcePath) && !Path.EndsInDirectorySeparator(mapped) ? mapped + sourcePath[^1] : mapped;
    }

    internal async Task<PhysicalGraphCopyPlan> PlanCopyAsync(string workspaceId, string destination,
        IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = new HashSet<string>(FileSystemPath.Comparer);
        var directories = new HashSet<string>(FileSystemPath.Comparer) { destination };
        var inputDirectories = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var directory in ContentRoots)
        {
            if (IsImmutablePath(directory)) continue;
            await AuthorizeAsync(directory).ConfigureAwait(false);
            using var held = PhysicalDirectoryScope.OpenRoute(directory);
            await EnrollDirectoryAsync(held).ConfigureAwait(false);
        }
        foreach (var input in ConditionInputs.Where(input => input.Kind == PhysicalInputKind.Directory))
            directories.Add(MapTo(destination, input.FilePath));
        foreach (var path in InputFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (IsImmutablePath(path) && !IsContentReplayFile(path)) continue;
            await AuthorizeAsync(path).ConfigureAwait(false);
            WorkspaceManager.EnsurePhysicalPath(path);
            if (!ExistingFile(path)) continue;
            if (WorkspaceForkApplyService.IsSecretBearingFile(Path.GetFileName(path)))
                throw new PublicInvalidOperationException("An authored build input is excluded from workspace isolation by the secret-file policy.");
            files.Add(path);
        }
        return new PhysicalGraphCopyPlan(files.Select(path => new PhysicalGraphCopyFile(path, MapTo(destination, path))).ToArray(), directories.ToArray(), inputDirectories.ToArray());

        Task AuthorizeAsync(string path) => pathPolicy.ValidateAsync(workspaceId, Packages.PathsRequiringCurrentGrant([path]), ct);

        async Task EnrollDirectoryAsync(PhysicalDirectoryScope directory)
        {
            await AuthorizeAsync(directory.Path).ConfigureAwait(false);
            directory.AssertCurrentPath();
            inputDirectories.Add(directory.Path);
            directories.Add(MapTo(destination, directory.Path));
            var entries = directory.Entries().Select(name => (Name: name,
                Kind: directory.GetEntryKind(name, ct) ?? throw new IOException("A graph source entry disappeared during planning."))).ToArray();
            // Preserve the original files-first, then directories traversal and native order.
            foreach (var entry in entries.Where(entry => entry.Kind == PhysicalDirectoryEntryKind.File))
            {
                var file = Path.Combine(directory.Path, entry.Name);
                if ((IsImmutablePath(file) && !IsContentReplayFile(file))
                    || entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || WorkspaceForkApplyService.IsSecretBearingFile(entry.Name)) continue;
                await AuthorizeAsync(file).ConfigureAwait(false);
                directory.AssertCurrentPath();
                if (directory.GetEntryKind(entry.Name, ct) != entry.Kind)
                    throw new IOException("A graph source file changed during planning.");
                files.Add(file);
            }
            foreach (var entry in entries.Where(entry => entry.Kind == PhysicalDirectoryEntryKind.Directory))
            {
                var childPath = Path.Combine(directory.Path, entry.Name);
                if (IsImmutablePath(childPath) || WorkspaceForkApplyService.IsCopyExcludedDirectory(entry.Name)) continue;
                await AuthorizeAsync(childPath).ConfigureAwait(false);
                directory.AssertCurrentPath();
                using var child = directory.OpenChild(entry.Name)
                    ?? throw new IOException("A graph source directory changed during planning.");
                await EnrollDirectoryAsync(child).ConfigureAwait(false);
            }
            directory.AssertCurrentPath();
        }
    }

    private static bool ExistingFile(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal string MapTo(string destination, string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Contains(Root, path))
        {
            var failure = new PublicInvalidOperationException("The graph path is outside its captured layout.");
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            failure.Data["LayoutMapFailureKind"] = FileSystemPath.Comparer.Equals(Root, normalized)
                ? "CoordinateRootSpelling" : Contains(Root, normalized) ? "ContainedPathSpelling" : "OutsideCoordinateRoot";
            failure.Data["LayoutMapDirectorySuffix"] = Path.EndsInDirectorySeparator(path);
            throw failure;
        }
        return Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(Root, path)));
    }

    internal string MapFrom(string destination, string path) => Contains(destination, path)
        ? Path.GetFullPath(Path.Combine(Root, Path.GetRelativePath(destination, path))) : path;

    private static bool Contains(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path);
    }
}

internal sealed record PhysicalGraphCopyFile(string Source, string Destination);

internal sealed record PhysicalGraphCopyPlan(IReadOnlyList<PhysicalGraphCopyFile> Files, IReadOnlyList<string> Directories, IReadOnlyList<string> InputDirectories)
{
    internal IReadOnlyList<string> Inventory => Files.SelectMany(file => new[] { file.Source, file.Destination })
        .Concat(Directories).Concat(InputDirectories).Distinct(FileSystemPath.Comparer).ToArray();

    internal async Task CopyAsync(PhysicalDirectoryScope.OwnedRoot owner, CancellationToken ct)
    {
        foreach (var directory in Directories)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(owner.Path, directory);
            owner.EnsureDirectories(relative == "." ? string.Empty : relative, ct);
        }
        foreach (var file in Files)
        {
            ct.ThrowIfCancellationRequested();
            await using var source = PhysicalInputReader.OpenRead(file.Source, ct);
            await owner.CopyFromStreamAsync(Path.GetRelativePath(owner.Path, file.Destination), file.Source, source.Stream, ct).ConfigureAwait(false);
        }
    }
}
