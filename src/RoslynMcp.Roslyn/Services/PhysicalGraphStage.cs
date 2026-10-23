using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Loads current physical state and owned recovery overlays in an isolated native graph.</summary>
internal sealed class PhysicalGraphStage : IDisposable
{
    private readonly DirectoryScope _directoryScope;
    private readonly PhysicalGraphCandidate _candidate;
    private readonly MsBuildCommandProperties.Redactor _redactor;
    private readonly Func<string, string> _mapFromSource;
    private readonly ForkInvocationAuthority _operation;
    private readonly IDisposable? _principalScope;
    private readonly List<IDisposable> _readScopes = [];
    private bool _disposed;
    internal ContentReplayAuthority ReplayAuthority { get; }

    private PhysicalGraphStage(DirectoryScope directoryScope, PhysicalGraphCandidate candidate,
        PhysicalGraphExpectation expected, PhysicalGraphLayout layout, Func<string, string> mapFromSource,
        MsBuildCommandProperties.Redactor redactor, ContentReplayAuthority replayAuthority,
        ForkInvocationAuthority operation, IDisposable? principalScope)
    {
        _directoryScope = directoryScope;
        _candidate = candidate;
        Expected = expected;
        Layout = layout;
        _mapFromSource = mapFromSource;
        _redactor = redactor;
        ReplayAuthority = replayAuthority;
        _operation = operation;
        _principalScope = principalScope;
    }

    internal PhysicalGraphExpectation Expected { get; }
    internal PhysicalGraphLayout Layout { get; }
    internal PhysicalDirectoryCleanupOutcome? CleanupOutcome => _directoryScope.Outcome;
    internal string MapFromSource(string path) => _mapFromSource(path);

    internal void AssertCustodyObservationCurrent(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PhysicalGraphStage));
        _directoryScope.Owner.AssertCurrentPath(ct);
    }

    internal void EnterReadAuthority()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PhysicalGraphStage));
        var active = ForkInvocationPathPolicy.ActiveAuthority;
        if (active is null) _readScopes.Add(_operation.Policy.Enter(_operation));
        else if (!ReferenceEquals(active, _operation))
            throw new WorkspaceMutationOwnershipException("The staged graph belongs to another operation authority.");
        _readScopes.Add(_operation.EnterOwnedCopy(_directoryScope.Owner, Layout));
    }

    internal static async Task<PhysicalGraphStage> CreateAsync(string workspaceId, string loadedPath,
        Solution actualSource, PhysicalGraphLayout layout, IReadOnlyList<FileSnapshotDto> ownedPreOverlay,
        IReadOnlyDictionary<string, string> globals, WorkspaceSessionLoader loader,
        IWorkspacePathPolicy pathPolicy, ForkInvocationAuthority operation, ILogger logger, CancellationToken ct)
    {
        if (!ReferenceEquals(pathPolicy, operation.Policy))
            throw new WorkspaceMutationOwnershipException("Staging and its issuing operation must share the exact path policy.");
        IDisposable? principalScope = null;
        var active = ForkInvocationPathPolicy.ActiveAuthority;
        if (active is not null && !ReferenceEquals(active, operation))
            throw new WorkspaceMutationOwnershipException("The staging operation does not own the active path authority.");
        var stagingRoot = operation.StageParentPath;
        var directory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        var discoveryDirectory = Path.Combine(directory, ".graph-discovery");
        var discoveryPath = Path.Combine(discoveryDirectory, Path.GetFileName(loadedPath));
        PhysicalGraphCandidate? candidate = null;
        DirectoryScope? directoryScope = null;
        var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], globals));
        var sourceInputs = new Dictionary<string, PhysicalInputIdentity>(FileSystemPath.Comparer);
        try
        {
            if (active is null) principalScope = operation.Policy.Enter(operation);
            IEnumerable<string> declaredInputs = actualSource.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments)
                .Select(document => document.FilePath).OfType<string>()
                .Concat(actualSource.Projects.SelectMany(project => project.MetadataReferences)
                    .OfType<PortableExecutableReference>().Select(reference => reference.FilePath).OfType<string>())
                .Concat(actualSource.Projects.SelectMany(project => project.AnalyzerReferences)
                    .Select(reference => reference.FullPath).OfType<string>());
            // Authorize directory enumeration and declared mutable inputs before inspecting
            // descendants. The common coordinate root is not an ownership grant.
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(layout.ContentRoots.Concat(layout.InputFiles).Concat(layout.ConditionInputs.Select(input => input.FilePath))
                .Concat(declaredInputs.Where(path => !IsImmutable(path))).Concat(ownedPreOverlay.Select(file => file.FilePath))
                .Distinct(FileSystemPath.Comparer)), ct).ConfigureAwait(false);
            await PhysicalInputIdentity.AssertCurrentAsync(layout.ConditionInputs, ct).ConfigureAwait(false);
            SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
            await layout.Packages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            await CaptureSourceInputsAsync(layout.InputFiles).ConfigureAwait(false);
            // Relocation rewrites private copies, never the source evaluation. Capture
            // its exact selected contexts once for this stage; all source-input and
            // candidate authority checks remain fresh around the relocation passes.
            EvaluatedBuildGraph.Graph sourceGraph = EvaluatedBuildGraph.Source(actualSource, ImmutableDictionary<string, byte[]>.Empty, ct);
            var generatedConfigurations = actualSource.Projects.Select(project =>
            {
                EvaluatedBuildGraph.Node node = EvaluatedBuildGraph.Resolve(project, sourceGraph);
                return node.GeneratedEditorConfigPath;
            }).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(
                layout.ContentRoots.Concat(layout.InputFiles).Concat(layout.ConditionInputs
                    .Where(input => input.Kind == PhysicalInputKind.Directory).Select(input => input.FilePath))), ct).ConfigureAwait(false);
            PhysicalGraphCopyPlan plan = await layout.PlanCopyAsync(workspaceId, directory, pathPolicy, ct).ConfigureAwait(false);
            var files = plan.Files.ToDictionary(file => file.Source, FileSystemPath.Comparer);
            var inputs = declaredInputs.Concat(layout.BuildNodes.SelectMany(node => new[] { node.Inputs.AssetsPath,
                    node.Inputs.ExtensionsPath is { } extensions ? Path.Combine(extensions, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json") : null })
                    .OfType<string>()).Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, inputs.Where(path => !IsImmutable(path)).ToArray(), ct).ConfigureAwait(false);
            foreach (var input in inputs.Distinct(FileSystemPath.Comparer))
            {
                if (IsImmutable(input)) continue;
                if (!Contains(layout.Root, input))
                    throw new PublicInvalidOperationException("A mutable graph input is outside the approved staging layout.");
                WorkspaceManager.EnsurePhysicalPath(input);
                if (ExistingFile(input)) files.TryAdd(input, new(input, layout.MapTo(directory, input)));
            }
            // Package imports remain external. Exact native project-item members
            // also need a private content copy so SDK globs and RAR retain them.
            foreach (var input in files.Keys.ToArray())
                if (IsImmutable(input) && !layout.IsContentReplayFile(input) || !ExistingFile(input)) files.Remove(input);
            var overlays = ownedPreOverlay.ToDictionary(file => Path.GetFullPath(file.FilePath), FileSystemPath.Comparer);
            foreach (var path in overlays.Keys)
            {
                if (!Contains(layout.Root, path) || IsImmutable(path))
                    throw new PublicInvalidOperationException("An owned recovery file is outside the mutable staging layout.");
            }

            var directories = plan.Directories.Concat(files.Values.Select(file => Path.GetDirectoryName(file.Destination)!))
                .Concat(overlays.Keys.Select(path => Path.GetDirectoryName(layout.MapTo(directory, path))!))
                .Append(discoveryDirectory)
                .Distinct(FileSystemPath.Comparer).ToArray();
            plan = new PhysicalGraphCopyPlan(files.Values.ToArray(), directories, plan.InputDirectories);
            var inventory = plan.Files.Select(file => file.Source).Concat(plan.InputDirectories).Concat(overlays.Keys)
                .Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(inventory), ct).ConfigureAwait(false);

            // Freeze all build definitions before effects; the copy plan never rescans after approval.
            var definitions = new Dictionary<string, byte[]>(FileSystemPath.Comparer);
            var globalJsonFiles = new Dictionary<string, byte[]>(FileSystemPath.Comparer);
            foreach (PhysicalGraphCopyFile? file in files.Values.Where(file => !layout.IsImmutablePath(file.Source) && Path.GetFileName(file.Source).Equals("global.json", StringComparison.OrdinalIgnoreCase)))
                globalJsonFiles.Add(file.Source, await PhysicalInputReader.ReadAllBytesAsync(file.Source, ct).ConfigureAwait(false));
            foreach (PhysicalGraphCopyFile? file in files.Values.Where(file => !layout.IsImmutablePath(file.Source) && IsBuildDefinition(file.Source)))
            {
                definitions.Add(file.Source, await PhysicalInputReader.ReadAllBytesAsync(file.Source, ct).ConfigureAwait(false));
            }
            IReadOnlySet<string> pathProperties = BuildPathRelocation.KnownPathProperties;
            string MapToStage(string path) => overlays.ContainsKey(Path.GetFullPath(path)) ? layout.MapTo(directory, path) : layout.MapInputPath(directory, path);
            string MapToSource(string path) => layout.MapFrom(directory, path);
            var stageGlobals = globals.ToDictionary(pair => pair.Key,
                pair => BuildPathRelocation.RelocateGlobal(pair.Key, pair.Value, MapToStage, pathProperties), StringComparer.OrdinalIgnoreCase);
            directoryScope = await DirectoryScope.CreateAsync(workspaceId, actualSource, layout, directory, stagingRoot, pathPolicy, logger, ct).ConfigureAwait(false);
            directoryScope.Enter(operation, layout);
            ContentReplayAuthority replayAuthority = await ContentReplayAuthority.CaptureAsync(workspaceId, layout, directory, pathPolicy, ct).ConfigureAwait(false);
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(plan.Inventory), ct).ConfigureAwait(false);
            await plan.CopyAsync(CurrentOwner(), ct).ConfigureAwait(false);
            await RelocateAsync(definitions).ConfigureAwait(false);
            await AssertSourceInputsAsync().ConfigureAwait(false);
            PhysicalGraphCandidate baseline = await PhysicalGraphCandidate.LoadAsync(workspaceId, MapToStage(loadedPath), stageGlobals, pathPolicy, loader, logger, null, ct).ConfigureAwait(false);
            try
            {
                await replayAuthority.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
                await SolutionFileMutationPlanner.AssertEquivalentAsync(actualSource, baseline.Solution,
                    MapToStage, replayAuthority.MapActual, verifyDocuments: true, pathProperties, ct).ConfigureAwait(false);
                await replayAuthority.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Staged baseline validation and cleanup failed.", failure, baseline);
                throw;
            }
            baseline.Dispose();

            await ApplyOverlaysAsync().ConfigureAwait(false);
            await RelocateAsync(definitions).ConfigureAwait(false);

            // MSBuild evaluation is the repository's trusted-workspace boundary, not a
            // filesystem sandbox. Discovery may resolve new native inputs; our explicit
            // enumeration/reads/copy still require approval before enrolling those inputs.
            var evaluatedDefinitions = definitions.Where(pair => IsMsBuildDefinition(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, FileSystemPath.Comparer);
            EvaluatedBuildGraph.Graph desired;
            if (loadedPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || loadedPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                WorkspaceManager.EnsurePhysicalPath(discoveryPath);
                if (!definitions.TryGetValue(loadedPath, out var solutionBytes))
                    throw new PublicInvalidOperationException("Recovery cannot remove the loaded solution entry point.");
                var discoveryBytes = SolutionFileMutationPlanner.RenderSolutionCoordinates(loadedPath, solutionBytes, static path => path);
                await CurrentOwner().WriteFileAsync(Path.GetRelativePath(directory, discoveryPath), discoveryBytes, ct).ConfigureAwait(false);
                desired = EvaluatedBuildGraph.FromSolutionDiscovery(discoveryPath, loadedPath, globals, evaluatedDefinitions, ct);
            }
            else
            {
                desired = EvaluatedBuildGraph.FromRoots([loadedPath], globals, evaluatedDefinitions, ct);
            }

            var desiredCliDirectories = desired.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!)
                .Append(Path.GetDirectoryName(loadedPath)!).Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, desiredCliDirectories, ct).ConfigureAwait(false);
            var desiredConfiguration = new Dictionary<string, PhysicalInputIdentity>(FileSystemPath.Comparer);
            foreach (var cliDirectory in desiredCliDirectories)
            {
                PreparedDotnetTestContext context = await DotnetTestModeResolver.ResolveOverlayAsync(workspaceId, cliDirectory, overlays, pathPolicy, ct).ConfigureAwait(false);
                foreach (DotnetConfigurationIdentity observed in context.Candidates)
                {
                    var identity = new PhysicalInputIdentity(observed.FilePath, observed.Exists, observed.ContentHash);
                    if (desiredConfiguration.TryGetValue(identity.FilePath, out PhysicalInputIdentity? prior)
                        && (prior.Exists != identity.Exists || !prior.ContentHash.AsSpan().SequenceEqual(identity.ContentHash.AsSpan())))
                    {
                        throw new PublicInvalidOperationException("Dotnet configuration changed during staged graph discovery. Retry preparation.");
                    }

                    desiredConfiguration[identity.FilePath] = identity;
                }
            }
            var desiredAssets = desired.Nodes.Select(node => node.Inputs.AssetsPath).OfType<string>()
                .Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, desiredAssets, ct).ConfigureAwait(false);
            InstalledPackageAuthority desiredPhysicalPackages = await BuildInputProvenance.ReadPackagesAsync(desiredAssets.Select(path => overlays.ContainsKey(path) ? MapToStage(path) : path), desired.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(), (paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            InstalledPackageAuthority desiredPackageProof = desiredPhysicalPackages.MapSelectingAssets(MapToSource).RetainPackageFilesFrom(layout.Packages);
            // The source has not received owned overlays. Its selecting receipts
            // must remain bound to source bytes while desired receipts follow the
            // private copies, then become the expected publication epoch.
            InstalledPackageAuthority discoveredSourcePackages = await BuildInputProvenance.ReadPackagesAsync(desiredAssets,
                desired.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(),
                (paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            InstalledPackageAuthority sourcePackages = InstalledPackageAuthority.Merge([layout.Packages, discoveredSourcePackages])
                .RetainPackageFilesFrom(desiredPackageProof);
            ImmutableArray<string> desiredPackages = desiredPackageProof.Files;
            IEnumerable<string> desiredImmutableDirectories = layout.ImmutableRoots.Concat(desired.Nodes.SelectMany(node => node.BuildInputs.ImmutableRoots));
            var desiredAuthored = layout.AuthoredRoots.Concat(desired.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!))
                .Distinct(FileSystemPath.Comparer).ToArray();
            var desiredProvenance = new ImmutablePathProvenance(desiredImmutableDirectories, layout.Provenance.ExactFiles.Concat(desiredPackages), desiredAuthored);
            bool DesiredImmutable(string path) => desiredProvenance.IsImmutable(path);
            SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
            ImmutableArray<SdkExistenceInput> desiredSdkInputs = SdkExistenceInput.Capture(desired.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes)
                .Where(SdkExistenceInput.Supports), ct);
            SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
            BuildPathRoles.ConditionProbe[] desiredProbes = [.. desired.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes
                .Where(probe => !SdkExistenceInput.Supports(probe) && !DesiredImmutable(probe.Path) && (!DesiredImmutable(probe.Origin)
                    || BuildPathRoles.IsNativeInputPath(node.PathEvidence, probe.Path))))];
            var desiredProbePaths = desiredProbes.Select(probe => probe.Path).Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, desiredProbePaths, ct).ConfigureAwait(false);
            var desiredConditions = new List<PhysicalInputIdentity>();
            foreach (var path in desiredProbePaths)
            {
                // Discovery observes current source coordinates, before the private
                // owned overlay changes their kind. Bind that observation to this
                // authorized source read, never to the later intended overlay.
                await pathPolicy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
                PhysicalInputIdentity observedSource = await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
                ConditionInputTruth.AssertObserved(desiredProbes.Where(probe => FileSystemPath.Comparer.Equals(probe.Path, path)), [observedSource]);
                if (overlays.TryGetValue(path, out FileSnapshotDto? overlay))
                {
                    desiredConditions.Add(new(path, overlay.OriginalText is not null, overlay.OriginalText is null ? []
                        : [.. SHA256.HashData(Bytes(overlay))]));
                }
                else desiredConditions.Add(observedSource);
            }
            var desiredDirectories = desired.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!)
                .Where(path => !DesiredImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
            var desiredDefinitions = desired.Nodes.SelectMany(node => node.BuildInputs.Definitions)
                .Where(path => !DesiredImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
            var desiredArtifacts = desired.Nodes.SelectMany(node => node.ArtifactFiles.Concat(node.DocumentFiles)
                    .Concat(new[] { node.TargetPath, node.Inputs.AssetsPath,
                        node.Inputs.ExtensionsPath is { } extension ? Path.Combine(extension, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json") : null }.OfType<string>()))
                .Where(path => !DesiredImmutable(path)).Distinct(FileSystemPath.Comparer).ToArray();
            await pathPolicy.ValidateAsync(workspaceId, desiredDirectories.Concat(desiredDefinitions).Concat(desiredArtifacts)
                .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
            layout = PhysicalGraphLayout.WithAdditionalInputs(layout, desiredDirectories, desiredDefinitions, desiredArtifacts, desired.Nodes, sourcePackages, desiredConfiguration.Values.ToArray(), desiredConditions, desiredSdkInputs);
            // POSIX cleanup retains directory names. A new coordinate prevents
            // those old directories from changing native Exists or glob results.
            directory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(
                layout.ContentRoots.Concat(layout.InputFiles).Concat(layout.ConditionInputs
                    .Where(input => input.Kind == PhysicalInputKind.Directory).Select(input => input.FilePath))), ct).ConfigureAwait(false);
            plan = await layout.PlanCopyAsync(workspaceId, directory, pathPolicy, ct).ConfigureAwait(false);
            files = plan.Files.Where(file => (!DesiredImmutable(file.Source) || layout.IsContentReplayFile(file.Source)) && ExistingFile(file.Source))
                .ToDictionary(file => file.Source, FileSystemPath.Comparer);
            directories = [.. plan.Directories.Concat(files.Values.Select(file => Path.GetDirectoryName(file.Destination)!))
                .Concat(overlays.Keys.Select(path => Path.GetDirectoryName(layout.MapTo(directory, path))!))
                .Distinct(FileSystemPath.Comparer)];
            plan = new PhysicalGraphCopyPlan(files.Values.ToArray(), directories, plan.InputDirectories);
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(plan.Files.Select(file => file.Source)
                .Concat(plan.InputDirectories).Concat(layout.OutputDirectories).Concat(layout.MappingDirectories)
                .Concat(overlays.Keys).Distinct(FileSystemPath.Comparer)), ct).ConfigureAwait(false);
            await CaptureSourceInputsAsync(layout.InputFiles).ConfigureAwait(false);
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(files.Values
                .Where(file => !layout.IsImmutablePath(file.Source) && (IsBuildDefinition(file.Source)
                    || Path.GetFileName(file.Source).Equals("global.json", StringComparison.OrdinalIgnoreCase)))
                .Select(file => file.Source)), ct).ConfigureAwait(false);
            definitions.Clear();
            globalJsonFiles.Clear();
            foreach (PhysicalGraphCopyFile? file in files.Values.Where(file => !layout.IsImmutablePath(file.Source) && Path.GetFileName(file.Source).Equals("global.json", StringComparison.OrdinalIgnoreCase)))
                globalJsonFiles.Add(file.Source, await PhysicalInputReader.ReadAllBytesAsync(file.Source, ct).ConfigureAwait(false));
            foreach (PhysicalGraphCopyFile? file in files.Values.Where(file => !layout.IsImmutablePath(file.Source) && IsBuildDefinition(file.Source)))
            {
                definitions.Add(file.Source, await PhysicalInputReader.ReadAllBytesAsync(file.Source, ct).ConfigureAwait(false));
            }
            pathProperties = BuildPathRelocation.KnownPathProperties;
            stageGlobals = globals.ToDictionary(pair => pair.Key,
                pair => BuildPathRelocation.RelocateGlobal(pair.Key, pair.Value, MapToStage, pathProperties), StringComparer.OrdinalIgnoreCase);
            await desiredPhysicalPackages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            directoryScope.Dispose();
            directoryScope = null;
            directoryScope = await DirectoryScope.CreateAsync(workspaceId, actualSource, layout, directory, stagingRoot, pathPolicy, logger, ct).ConfigureAwait(false);
            directoryScope.Enter(operation, layout);
            replayAuthority = await ContentReplayAuthority.CaptureAsync(workspaceId, layout, directory, pathPolicy, ct).ConfigureAwait(false);
            await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(plan.Inventory), ct).ConfigureAwait(false);
            await plan.CopyAsync(CurrentOwner(), ct).ConfigureAwait(false);
            await ApplyOverlaysAsync().ConfigureAwait(false);
            await RelocateAsync(definitions).ConfigureAwait(false);
            await AssertSourceInputsAsync().ConfigureAwait(false);
            desiredPhysicalPackages = desiredPackageProof.MapSelectingAssets(MapToStage);
            await desiredPhysicalPackages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            candidate = await PhysicalGraphCandidate.LoadAsync(workspaceId, MapToStage(loadedPath), stageGlobals, pathPolicy, loader, logger, null, ct).ConfigureAwait(false);
            IEnumerable<string> candidatePaths = candidate.Solution.Projects.Select(project => project.FilePath).OfType<string>()
                .Concat(candidate.Solution.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments).Select(document => document.FilePath).OfType<string>())
                .Concat(candidate.Solution.Projects.SelectMany(project => project.MetadataReferences).OfType<PortableExecutableReference>()
                    .Select(reference => reference.FilePath).OfType<string>())
                .Concat(candidate.Solution.Projects.SelectMany(project => project.AnalyzerReferences).Select(reference => reference.FullPath).OfType<string>());
            if (candidatePaths.Any(path => !Contains(directory, path) && !DesiredImmutable(path)))
                throw new PublicInvalidOperationException("The recovered native graph contains a mutable input outside its approved copied inventory.");
            foreach (TextDocument? document in candidate.Solution.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments))
                await document.GetTextAsync(ct).ConfigureAwait(false);
            // Capturing execution identity evaluates native project inputs. Complete
            // that work before the final source, package and replay freshness checks.
            var executionIdentity = EvaluatedExecutionIdentity.Capture(candidate.Solution, MapToSource, pathProperties, ct);
            if (NativeCompilerContextCertificate.TryGet(candidate.Solution, out NativeCompilerContextCertificate? finalContext)
                && finalContext!.Availability == NativeCompilerContextAvailability.Certified)
            {
                await finalContext!.AssertCurrentAsync(candidate.Solution, workspaceId, pathPolicy, ct).ConfigureAwait(false);
            }

            await AssertSourceInputsAsync().ConfigureAwait(false);
            await desiredPhysicalPackages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            var expectedInputs = new Dictionary<string, PhysicalInputIdentity>(sourceInputs, FileSystemPath.Comparer);
            foreach (PhysicalInputIdentity input in PhysicalInputIdentity.MergeConsistent(layout.ConfigurationInputs.Concat(layout.ConditionInputs).Concat(desiredPackageProof.Inputs)))
            {
                if (expectedInputs.TryGetValue(input.FilePath, out PhysicalInputIdentity? sourceInput) && !overlays.ContainsKey(input.FilePath))
                    _ = PhysicalInputIdentity.MergeConsistent([sourceInput, input]);
                expectedInputs[input.FilePath] = input;
            }
            foreach ((string? path, byte[]? bytes) in definitions.Concat(globalJsonFiles))
                expectedInputs[path] = new(path, true, [.. SHA256.HashData(bytes)]);
            foreach ((string? path, FileSnapshotDto? overlay) in overlays)
            {
                expectedInputs[path] = overlay.OriginalText is null ? new(path, false, [])
                    : new(path, true, [.. SHA256.HashData(Bytes(overlay))]);
            }

            await replayAuthority.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            var expectedBuildInputs = expectedInputs.Values.ToImmutableArray();
            return new PhysicalGraphStage(directoryScope, candidate,
                new(candidate.Solution, MapToSource, pathProperties,
                executionIdentity, expectedBuildInputs, desiredPackageProof, layout.SdkInputs), layout, MapToStage, redactor, replayAuthority,
                operation, principalScope);

            async Task CaptureSourceInputsAsync(IEnumerable<string> paths)
            {
                foreach (var path in paths.Distinct(FileSystemPath.Comparer))
                {
                    if (sourceInputs.ContainsKey(path)) continue;
                    await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant([path]), ct).ConfigureAwait(false);
                    sourceInputs.Add(path, await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
                }
                await AssertSourceInputsAsync().ConfigureAwait(false);
            }

            async Task AssertSourceInputsAsync()
            {
                SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
                await layout.Packages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
                foreach (PhysicalInputIdentity input in sourceInputs.Values)
                {
                    await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant([input.FilePath]), ct).ConfigureAwait(false);
                    await PhysicalInputIdentity.AssertCurrentAsync([input], ct).ConfigureAwait(false);
                }
                SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
            }

            async Task ApplyOverlaysAsync()
            {
                foreach ((string? path, FileSnapshotDto? overlay) in overlays)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = MapToStage(path);
                    var relative = Path.GetRelativePath(directory, target);
                    if (overlay.OriginalText is null)
                    {
                        CurrentOwner().DeleteFileIfPresent(relative, ct);
                        definitions.Remove(path);
                        globalJsonFiles.Remove(path);
                    }
                    else
                    {
                        var bytes = Bytes(overlay);
                        await CurrentOwner().WriteFileAsync(relative, bytes, ct).ConfigureAwait(false);
                        if (IsBuildDefinition(path)) definitions[path] = bytes;
                        else if (Path.GetFileName(path).Equals("global.json", StringComparison.OrdinalIgnoreCase)) globalJsonFiles[path] = bytes;
                    }
                }
            }
            PhysicalDirectoryScope.OwnedRoot CurrentOwner() => directoryScope?.Owner
                ?? throw InvalidOperationErrors.Internal("The current staged namespace has no live ownership authority.");
            bool IsImmutable(string path) => layout.IsImmutablePath(path);
            async Task RelocateAsync(IReadOnlyDictionary<string, byte[]> buildFiles)
            {
                foreach ((string? path, byte[]? bytes) in buildFiles)
                {
                    var target = MapToStage(path);
                    var relocated = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, MapToStage,
                        value => layout.MapDefinitionPath(directory, value), pathProperties, layout.BuildNodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
                    await CurrentOwner().WriteFileAsync(Path.GetRelativePath(directory, target), relocated, ct).ConfigureAwait(false);
                }
                foreach ((string? path, byte[]? bytes) in globalJsonFiles)
                {
                    var target = MapToStage(path);
                    await CurrentOwner().WriteFileAsync(Path.GetRelativePath(directory, target), bytes, ct).ConfigureAwait(false);
                }
                foreach (var generated in generatedConfigurations)
                {
                    if (IsImmutable(generated) || !Contains(layout.Root, generated)) continue;
                    var target = MapToStage(generated);
                    var relative = Path.GetRelativePath(directory, target);
                    var bytes = await CurrentOwner().ReadFileIfPresentAsync(relative, ct).ConfigureAwait(false);
                    if (bytes is null) continue;
                    // Staged bytes are current during baseline validation and contain only
                    // explicitly owned replacements after overlay application.
                    CsprojSemanticEquality.ProjectFileSnapshot text = CsprojSemanticEquality.CreateSnapshot(bytes);
                    var relocated = SolutionFileMutationPlanner.RelocateGeneratedConfig(text.Content, MapToStage, pathProperties);
                    await CurrentOwner().WriteFileAsync(relative, Encode(relocated, bytes), ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception failure)
        {
            try
            {
                OwnedResourceCleanup.Dispose("Staged graph preparation and cleanup failed.", failure, candidate,
                directoryScope, principalScope);
            }
            catch (Exception cleanup) { throw redactor.Failure(cleanup); }
            throw redactor.Failure(failure);
        }
    }

    private static bool ExistingFile(string path)
    {
        WorkspaceManager.EnsurePhysicalPath(path);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new PublicInvalidOperationException("A declared physical graph file identifies a directory.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsMsBuildDefinition(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    private static bool IsBuildDefinition(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string root, string path) => FileSystemPath.Comparer.Equals(root, path)
        || FileSystemPath.IsStrictDescendant(root, path);
    private static byte[] Bytes(FileSnapshotDto snapshot) => !snapshot.OriginalBytes.IsDefault
        ? [.. snapshot.OriginalBytes] : Encoding.UTF8.GetBytes(snapshot.OriginalText!);
    private static byte[] Encode(string text, byte[] original)
    {
        Encoding encoding = SourceFileEncoding.FromBytes(original);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { OwnedResourceCleanup.Dispose("Failed to release staged graph resources.", null,
            new IDisposable?[] { _candidate, _directoryScope }.Concat(_readScopes.AsEnumerable().Reverse())
                .Append(_principalScope).ToArray()); }
        catch (Exception failure) { throw _redactor.Failure(failure); }
    }
    private sealed class DirectoryScope(PhysicalDirectoryScope.OwnedRoot owned, ILogger logger) : IDisposable
    {
        private bool _disposed;
        private IDisposable? _copyScope;
        internal PhysicalDirectoryCleanupOutcome? Outcome { get; private set; }
        internal PhysicalDirectoryScope.OwnedRoot Owner => owned;
        internal void Enter(ForkInvocationAuthority operation, PhysicalGraphLayout layout) => _copyScope = operation.EnterOwnedCopy(owned, layout);
        internal static async Task<DirectoryScope> CreateAsync(string workspaceId, Solution source, PhysicalGraphLayout layout,
            string directory, string stagingRoot, IWorkspacePathPolicy pathPolicy, ILogger logger, CancellationToken ct)
        {
            if (!FileSystemPath.IsStrictDescendant(stagingRoot, directory)
                || !StringComparer.Ordinal.Equals(Path.GetFileName(stagingRoot), "roslynmcp-staging"))
                throw InvalidOperationErrors.Internal("A staged graph cleanup escaped its owned directory.");
            var parent = await PhysicalGraphNamespace.OpenParentAsync(workspaceId, source, layout, stagingRoot, directory, pathPolicy, ct).ConfigureAwait(false);
            var owner = PhysicalDirectoryScope.CreateOwnedChild(parent, Path.GetFileName(directory));
            try { owner.AssertCurrentPath(ct); return new(owner, logger); }
            catch (Exception failure)
            {
                OwnedResourceCleanup.DisposePreservingFailure("Staged graph admission cleanup failed.", failure, new DirectoryScope(owner, logger));
                throw;
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? primary = null;
            try
            {
                Outcome = owned.Delete(CancellationToken.None);
                if (Outcome == PhysicalDirectoryCleanupOutcome.Retained)
                    logger.LogWarning("Staged graph content was cleaned; directory names were retained because final removal requires exclusive namespace ownership.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Failed to release staged graph cleanup ownership.", primary, owned, _copyScope); }
        }
    }
}
