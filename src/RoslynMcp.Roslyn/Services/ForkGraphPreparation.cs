using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Captures current source authority and renders the reviewed delta before fork side effects.</summary>
internal sealed record ForkGraphPreparation(
    Solution Expected,
    Solution InitialExpected,
    IReadOnlyList<SolutionFileMutationPlanner.Mutation> Mutations,
    IReadOnlyList<RestoreInvocationPlanner.ProjectRestore> SourceProjects,
    IReadOnlyDictionary<string, byte[]> BuildFiles,
    IReadOnlySet<string> PathProperties, PhysicalGraphLayout Layout, PhysicalGraphCopyPlan CopyPlan,
    RestoredCompilerInputAuthority CompilerInputs, ContentReplayAuthority ReplayAuthority)
{
    internal static async Task<ForkGraphPreparation> CreateAsync(
        string workspaceId, string loadedPath, Solution original, Solution modified, Solution current,
        PhysicalGraphLayout layout, string forkRoot, IWorkspacePathPolicy pathPolicy,
        ForkInvocationAuthority stageOperation, ILogger logger, CancellationToken ct)
    {
        var sourceRoot = layout.Root;
        SolutionFileMutationPlanner.ValidateDelta(original, modified);
        Solution expected = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, modified, current, ct).ConfigureAwait(false);
        WorkspaceManager.EnsureChangedDocumentPathsArePhysical(current, expected);
        await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(layout.ContentRoots.Concat(layout.InputFiles)
            .Concat(SolutionFileMutationPlanner.ChangedPaths(current, expected))
            .Concat(current.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments)
                .Select(document => document.FilePath).OfType<string>().Where(path => !layout.IsImmutablePath(path)))
            .Distinct(FileSystemPath.Comparer)), ct).ConfigureAwait(false);
        RestoredCompilerInputAuthority compilerInputs = await RestoredCompilerInputAuthority.CaptureAsync(workspaceId, current,
            static path => path, pathPolicy, RestoreArtifactPhase.BeforeRestore, ct).ConfigureAwait(false);
        CompilerReferenceAuthority referenceAuthority = compilerInputs.ReferenceAuthority();
        await SolutionFileMutationPlanner.AssertCurrentDiskAuthorityAsync(current, referenceAuthority, ct).ConfigureAwait(false);
        var paths = new HashSet<string>(FileSystemPath.Comparer);
        EvaluatedBuildGraph.Graph? expectedGraph = null;
        foreach (Project project in expected.Projects)
        {
            if (project.FilePath is null || !paths.Add(PhysicalProjectIdentity.Key(project, static path => path)))
                throw new PublicInvalidOperationException("A fork project identity is missing or ambiguous across target frameworks.");
            if (expected.Projects.Count(candidate => FileSystemPath.Comparer.Equals(candidate.FilePath, project.FilePath)) > 1)
            {
                expectedGraph ??= EvaluatedBuildGraph.Source(expected, ImmutableDictionary<string, byte[]>.Empty, ct);
                _ = EvaluatedBuildGraph.Resolve(project, expectedGraph);
            }
            CheckPath(project.FilePath, sourceRoot);
            if (!File.Exists(project.FilePath))
                throw new PublicInvalidOperationException("The reviewed graph includes a project without an authored project file.");
            foreach (TextDocument document in SolutionFileMutationPlanner.AllDocuments(project))
            {
                if (document.FilePath is { } path)
                {
                    WorkspaceManager.EnsurePhysicalPath(path);
                    if (!layout.IsImmutablePath(path)) CheckPath(path, sourceRoot);
                }
            }
        }
        IReadOnlyList<SolutionFileMutationPlanner.Mutation> mutations = await SolutionFileMutationPlanner.CreateAsync(current, expected,
            static async (path, token) => await FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, token).ConfigureAwait(false),
            referenceAuthority, includeDocuments: true, allowVirtualPaths: false, ct).ConfigureAwait(false);
        expected = SolutionFileMutationPlanner.WithPhysicalDocumentText(expected, mutations);
        PhysicalInputIdentity[] originalInputs = [.. mutations.Select(mutation => Identity(mutation.Path, mutation.Before))];
        await PhysicalInputIdentity.AssertCurrentAsync(originalInputs, ct).ConfigureAwait(false);
        FileSnapshotDto[] overlays = [.. mutations.Select(mutation => mutation.After is null
            ? new FileSnapshotDto(mutation.Path, null)
            : FileSnapshotDto.FromExistingBytes(mutation.Path, mutation.After))];
        // Reuse the same isolated physical renderer used by recovery. Its actual native
        // desired graph honors solution membership, imports, and each edge's evaluated
        // context before package roots are captured or the final fork is created.
        using PhysicalGraphStage stage = await PhysicalGraphStage.CreateAsync(workspaceId, loadedPath, current, layout, overlays,
            EvaluatedProjectGraph.Properties(current), new WorkspaceSessionLoader(), pathPolicy, stageOperation, logger, ct).ConfigureAwait(false);
        stage.EnterReadAuthority();
        RestoredCompilerInputAuthority desiredCompilerInputs = await RestoredCompilerInputAuthority.CaptureAsync(workspaceId, stage.Expected.Solution,
            stage.Expected.MapPath, pathPolicy, RestoreArtifactPhase.BeforeRestore, ct).ConfigureAwait(false);
        await desiredCompilerInputs.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        expected = compilerInputs.WithPreRestoreCompilerReferences(expected, desiredCompilerInputs);
        await stage.ReplayAuthority.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, stage.Expected.Solution, stage.MapFromSource, stage.ReplayAuthority.MapActual,
            verifyDocuments: true, stage.Expected.PathProperties, ct).ConfigureAwait(false);
        await stage.ReplayAuthority.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        compilerInputs = compilerInputs.MergeAddedProjects(desiredCompilerInputs);
        layout = stage.Layout;
        sourceRoot = layout.Root;
        EvaluatedBuildGraph.Graph reviewedBuildGraph = EvaluatedBuildGraph.Source(stage.Expected.Solution, ct);
        RestoreInvocationPlanner.ProjectRestore[] roots = [.. RestoreInvocationPlanner.CaptureNodes(reviewedBuildGraph.Nodes, ct).Select(project =>
            new RestoreInvocationPlanner.ProjectRestore(stage.Expected.MapPath(project.ProjectPath), project.PackagesPath,
                project.TargetFramework, project.EvaluationProperties.ToImmutableDictionary(pair => pair.Key,
                    pair => BuildPathRelocation.RelocateGlobal(pair.Key, pair.Value, stage.Expected.MapPath, stage.Expected.PathProperties),
                    StringComparer.OrdinalIgnoreCase), project.AssetsPath is { } assets ? stage.Expected.MapPath(assets) : null))];
        foreach (RestoreInvocationPlanner.ProjectRestore project in roots)
        {
            CheckPath(project.ProjectPath, sourceRoot);
            if (!File.Exists(project.ProjectPath))
                throw new PublicInvalidOperationException("A reviewed build dependency has no authored project file.");
        }
        foreach (var path in mutations.Select(mutation => mutation.Path)) CheckPath(path, sourceRoot);
        var sourceInputs = stage.Expected.BuildInputs.ToDictionary(input => input.FilePath, FileSystemPath.Comparer);
        foreach (PhysicalInputIdentity? input in originalInputs) sourceInputs[input.FilePath] = input;
        PhysicalGraphExpectation sourceInputAuthority = stage.Expected with { BuildInputs = [.. sourceInputs.Values] };
        await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(layout.ContentRoots.Concat(layout.InputFiles)), ct).ConfigureAwait(false);
        await sourceInputAuthority.AssertBuildInputsAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        var reviewedWrites = mutations.Select(mutation => mutation.Path).ToHashSet(FileSystemPath.Comparer);
        await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(
            layout.ContentRoots.Concat(layout.InputFiles).Concat(layout.ConditionInputs
                .Where(input => input.Kind == PhysicalInputKind.Directory).Select(input => input.FilePath))), ct).ConfigureAwait(false);
        PhysicalGraphCopyPlan completeCopy = await layout.PlanCopyAsync(workspaceId, forkRoot, pathPolicy, ct).ConfigureAwait(false);
        // The preflight stage needs source restore artifacts to establish authority.
        // The executable fork obtains assets from restore. Retain the exact old
        // generated imports so package-authored topology remains evaluable first.
        PhysicalGraphCopyPlan copyPlan = completeCopy with
        {
            Files = completeCopy.Files.Where(file =>
            !compilerInputs.NuGetGeneratedInputs.Contains(file.Source) || reviewedWrites.Contains(file.Source)
            || file.Source.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || file.Source.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        var initiallyPresent = copyPlan.Files.Select(file => file.Destination).ToHashSet(FileSystemPath.Comparer);
        foreach (SolutionFileMutationPlanner.Mutation mutation in mutations)
        {
            var destination = layout.MapTo(forkRoot, mutation.Path);
            if (mutation.After is null) initiallyPresent.Remove(destination);
            else initiallyPresent.Add(destination);
        }
        var absentAssets = roots.Select(project => project.AssetsPath).OfType<string>()
            .Where(path => !initiallyPresent.Contains(layout.MapInputPath(forkRoot, path))).ToHashSet(FileSystemPath.Comparer);
        Solution initialExpected = desiredCompilerInputs.WithoutAbsentAssetsImplicitReferences(expected, absentAssets, compilerInputs);
        await pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(copyPlan.Inventory.Concat(mutations.SelectMany(mutation =>
            new[] { mutation.Path, layout.MapTo(forkRoot, mutation.Path) }))
            .Concat(layout.OutputDirectories).Concat(layout.MappingDirectories)
            .Concat(layout.OutputDirectories.Concat(layout.MappingDirectories).Select(path => layout.MapInputPath(forkRoot, path)))
            .Distinct(FileSystemPath.Comparer)), ct).ConfigureAwait(false);
        var buildFiles = new Dictionary<string, byte[]>(FileSystemPath.Comparer);
        foreach (var path in copyPlan.Files.Select(file => file.Source)
            .Where(path => !layout.IsImmutablePath(path) && (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
            .Distinct(FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            buildFiles[path] = await PhysicalInputReader.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        await sourceInputAuthority.AssertBuildInputsAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        await compilerInputs.AssertCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
        ContentReplayAuthority replayAuthority = await ContentReplayAuthority.CaptureAsync(workspaceId, layout, forkRoot, pathPolicy, ct).ConfigureAwait(false);
        return new ForkGraphPreparation(expected, initialExpected, mutations, roots, buildFiles,
            BuildPathRelocation.KnownPathProperties, layout, copyPlan, compilerInputs, replayAuthority);

        static PhysicalInputIdentity Identity(string path, byte[]? bytes) => bytes is null
            ? new(path, false, []) : new(path, true, [.. SHA256.HashData(bytes)]);
    }

    internal string MapInputPath(string path, string forkRoot) => Layout.MapInputPath(forkRoot, path);

    private static void CheckPath(string path, string root)
    {
        WorkspaceManager.EnsurePhysicalPath(path);
        if (!FileSystemPath.IsStrictDescendant(root, path))
            throw new PublicInvalidOperationException("The reviewed graph reaches outside the isolated source root.");
    }

    internal sealed record ReplayResult(IReadOnlyList<string> AppliedFiles, ImmutableArray<PhysicalInputIdentity> BuildInputs);

    internal async Task<ReplayResult> ReplayAsync(string forkRoot, PhysicalDirectoryScope.OwnedRoot owner, CancellationToken ct)
    {
        // Freeze one rendered plan for both replay and the later execution checks. In
        // particular, an unchanged copied definition must use these captured bytes,
        // rather than whatever a concurrent external edit left in the copy source.
        var writes = new Dictionary<string, byte[]?>(FileSystemPath.Comparer);
        foreach (SolutionFileMutationPlanner.Mutation mutation in Mutations)
            writes[Map(mutation.Path)] = mutation.After;
        foreach ((string? sourcePath, byte[]? sourceBytes) in BuildFiles)
        {
            SolutionFileMutationPlanner.Mutation? mutation = Mutations.SingleOrDefault(mutation => FileSystemPath.Comparer.Equals(mutation.Path, sourcePath));
            writes[Map(sourcePath)] = mutation is { After: null } ? null
                : SolutionFileMutationPlanner.RelocateDefinition(sourcePath, mutation?.After ?? sourceBytes,
                    path => Layout.MapInputPath(forkRoot, path), path => Layout.MapDefinitionPath(forkRoot, path),
                    PathProperties, Layout.BuildNodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
        }
        var identities = PhysicalInputIdentity.MergeConsistent(Layout.ConfigurationInputs.Concat(Layout.ConditionInputs)).ToDictionary(identity => Map(identity.FilePath),
            identity => identity with { FilePath = Map(identity.FilePath) }, FileSystemPath.Comparer);
        foreach (var generated in CompilerInputs.NuGetGeneratedInputs)
            identities[Map(generated)] = new(Map(generated), false, []);
        foreach ((string? path, byte[]? bytes) in writes.Where(pair => IsExecutionDefinition(pair.Key)
            || CompilerInputs.NuGetGeneratedInputs.Contains(Layout.MapFrom(forkRoot, pair.Key))))
        {
            identities[path] = bytes is null ? new(path, false, []) : new(path, true, [.. SHA256.HashData(bytes)]);
        }

        foreach ((string? path, byte[]? bytes) in writes)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(owner.Path, path);
            if (bytes is null) owner.DeleteFileIfPresent(relative, ct);
            else await owner.WriteFileAsync(relative, bytes, ct).ConfigureAwait(false);
        }
        return new(Mutations.Select(mutation => Map(mutation.Path)).Distinct(FileSystemPath.Comparer).ToArray(),
            [.. identities.Values]);

        string Map(string path) => Layout.MapTo(forkRoot, path);
    }

    private static bool IsExecutionDefinition(string path) => Path.GetFileName(path).Equals("global.json", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}
