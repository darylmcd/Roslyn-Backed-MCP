using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Stages the complete physical mutation plan before publishing loaded workspace authority.</summary>
internal sealed class DocumentSetPersistenceService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger _logger;
    private readonly IDocumentSetFileSystem _fileSystem;

    public DocumentSetPersistenceService(IWorkspaceManager workspace, ILogger logger)
        : this(workspace, logger, PhysicalDocumentSetFileSystem.Instance) { }

    internal DocumentSetPersistenceService(IWorkspaceManager workspace, ILogger logger, IDocumentSetFileSystem fileSystem)
    {
        _workspace = workspace;
        _logger = logger;
        _fileSystem = fileSystem;
    }

    public async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistAsync(
        string workspaceId, Solution currentSolution, Solution modifiedSolution,
        SolutionChanges solutionChanges, IReadOnlyList<FileSnapshotDto> beforeSnapshots,
        PhysicalMutationRecovery physicalOperation, CancellationToken ct)
    {
        try
        {
            var authority = _workspace as IWorkspaceRecoveryAuthority
                ?? throw InvalidOperationErrors.Internal("Physical persistence requires workspace-owned recovery authority.");
            var currentPaths = currentSolution.Projects.Select(project => project.FilePath).Append(currentSolution.FilePath)
                .Concat(currentSolution.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments).Select(document => document.FilePath))
                .OfType<string>().Where(path => physicalOperation.CapturedLayout is null || !physicalOperation.CapturedLayout.IsImmutablePath(path));
            await authority.AuthorizePathsAsync(workspaceId, currentPaths.Concat(beforeSnapshots.Select(file => file.FilePath))
                .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
            WorkspaceManager.EnsureChangedDocumentPathsArePhysical(currentSolution, modifiedSolution);
            var references = await RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(
                currentSolution, currentSolution.ProjectIds.ToArray(),
                (path, token) => ReadAuthorizedAsync(workspaceId, path, token),
                (path, token) => ReadDigestAuthorizedAsync(workspaceId, path, token),
                (paths, token) => authority.AuthorizePathsAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            await SolutionFileMutationPlanner.AssertCurrentDiskAuthorityAsync(currentSolution, references, ct).ConfigureAwait(false);
            var mutations = await SolutionFileMutationPlanner.CreateAsync(currentSolution, modifiedSolution,
                (path, token) => ReadAuthorizedAsync(workspaceId, path, token),
                references, includeDocuments: true, allowVirtualPaths: false, ct).ConfigureAwait(false);
            var snapshots = beforeSnapshots.ToDictionary(file => Path.GetFullPath(file.FilePath), FileSystemPath.Comparer);
            foreach (var mutation in mutations)
            {
                if (!snapshots.TryGetValue(Path.GetFullPath(mutation.Path), out var snapshot)
                    || (snapshot.OriginalText is null ? mutation.Before is not null
                        : mutation.Before is null || (!snapshot.OriginalBytes.IsDefault
                            ? !snapshot.OriginalBytes.AsSpan().SequenceEqual(mutation.Before)
                            : snapshot.OriginalText != CsprojSemanticEquality.CreateSnapshot(mutation.Before).Content)))
                    throw new PublicInvalidOperationException("Source bytes changed after undo preparation; reload before applying the preview.");
            }
            return await PersistMutationsAsync(workspaceId,
                SolutionFileMutationPlanner.WithPhysicalDocumentText(modifiedSolution, mutations), mutations, physicalOperation, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not IPublicMessageException
            && failure is (IOException or UnauthorizedAccessException or InvalidOperationException))
        {
            _logger.LogWarning(failure, "Failed to prepare changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
    }

    internal async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistMutationsAsync(
        string workspaceId, Solution? expected, IReadOnlyList<SolutionFileMutationPlanner.Mutation> exactPlan,
        PhysicalMutationRecovery physicalOperation, CancellationToken ct)
    {
        var touched = new List<SolutionFileMutationPlanner.Mutation>();
        IWorkspaceMutationLease? lease = null;
        IPhysicalRestorationAuthority? desiredGraph = null;
        Exception? primaryFailure = null;
        var recoveryAuthority = _workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("Physical persistence requires workspace-owned recovery authority.");
        try
        {
            await recoveryAuthority.AuthorizePathsAsync(workspaceId, exactPlan.Select(mutation => mutation.Path).ToArray(), ct).ConfigureAwait(false);
            foreach (var mutation in exactPlan) WorkspaceManager.EnsurePhysicalPath(mutation.Path);
            var mutations = exactPlan.Select(mutation => mutation with { Path = Path.GetFullPath(mutation.Path) })
                .Where(mutation => mutation.Before is null ? mutation.After is not null
                : mutation.After is null || !mutation.Before.AsSpan().SequenceEqual(mutation.After)).ToArray();
            var ownedFiles = physicalOperation.Files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
            foreach (var mutation in mutations)
                if (!ownedFiles.TryGetValue(mutation.Path, out var file)
                    || (mutation.Before is null ? file.OriginalText is not null
                        : file.OriginalBytes.IsDefault || !file.OriginalBytes.AsSpan().SequenceEqual(mutation.Before)))
                    throw InvalidOperationErrors.Internal("The physical plan must match its operation's exact captured file ownership.");
            recoveryAuthority.EnsureMutationAllowed(workspaceId, mutations.Select(mutation => mutation.Path).ToArray());
            await recoveryAuthority.AuthorizePathsAsync(workspaceId, mutations.Select(mutation => mutation.Path).ToArray(), ct).ConfigureAwait(false);
            foreach (var mutation in mutations)
            {
                WorkspaceManager.EnsurePhysicalPath(mutation.Path);
                var actual = await FileSnapshotCapture.ReadIfPresentAsync(mutation.Path, _fileSystem.ReadAllBytesAsync, ct).ConfigureAwait(false);
                if (mutation.Before is null ? actual is not null : actual is null || !mutation.Before.AsSpan().SequenceEqual(actual))
                    throw new PublicInvalidOperationException("Source bytes changed after transaction preparation; reload before applying changes.");
            }
            if (mutations.Length == 0) return (true, []);
            if (physicalOperation.WorkspaceId != workspaceId)
                throw InvalidOperationErrors.Internal("Physical mutation authority belongs to another workspace.");
            lease = await recoveryAuthority.BeginMutationAsync(physicalOperation, ct).ConfigureAwait(false);
            if (physicalOperation.CapturedLayout is not null)
            {
                var finalFiles = physicalOperation.Files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
                foreach (var mutation in mutations)
                    finalFiles[mutation.Path] = mutation.After is null ? new FileSnapshotDto(mutation.Path, null)
                        : FileSnapshotDto.FromExistingBytes(mutation.Path, mutation.After);
                desiredGraph = await recoveryAuthority.StageRestorationAsync(physicalOperation, finalFiles.Values.ToArray(),
                    physicalOperation.CapturedLayout, ct).ConfigureAwait(false);
                if (expected is not null)
                    await SolutionFileMutationPlanner.AssertEquivalentAsync(desiredGraph.Expected.Solution, expected,
                        desiredGraph.Expected.MapPath, static path => path, verifyDocuments: true,
                        desiredGraph.Expected.PathProperties, ct).ConfigureAwait(false);
            }
            foreach (var mutation in mutations)
            {
                ct.ThrowIfCancellationRequested();
                await PhysicalMutationFileGuard.AssertMatchesAsync(mutation.Path, mutation.Before,
                    (path, token) => ReadAuthorizedAsync(workspaceId, path, token), ct).ConfigureAwait(false);
                touched.Add(mutation);
                lease.BeforeWrite(mutation.After is null ? new FileSnapshotDto(mutation.Path, null)
                    : FileSnapshotDto.FromExistingBytes(mutation.Path, mutation.After));
                await WriteAsync(workspaceId, mutation.Path, mutation.Before, mutation.After, physicalOperation, ct).ConfigureAwait(false);
            }
            if (desiredGraph is not null)
                await recoveryAuthority.CommitRestorationAsync(physicalOperation, desiredGraph.Expected, retainedRecovery: false, ct).ConfigureAwait(false);
            else
                await recoveryAuthority.CommitMutationAsync(physicalOperation, expected, ct).ConfigureAwait(false);
            foreach (var mutation in mutations)
                await PhysicalMutationFileGuard.AssertMatchesAsync(mutation.Path, mutation.After,
                    (path, token) => ReadAuthorizedAsync(workspaceId, path, token), ct).ConfigureAwait(false);
            var byPath = mutations.ToDictionary(mutation => mutation.Path, FileSystemPath.Comparer);
            lease.Complete(physicalOperation.Files.Select(file => byPath.TryGetValue(file.FilePath, out var mutation)
                ? mutation.After is null ? new FileSnapshotDto(file.FilePath, null) : FileSnapshotDto.FromExistingBytes(file.FilePath, mutation.After)
                : file).ToArray());
            return (true, mutations.Select(mutation => mutation.Path).Distinct(FileSystemPath.Comparer).ToArray());
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            var failures = new List<Exception> { failure };
            foreach (var mutation in touched.AsEnumerable().Reverse())
            {
                try
                {
                    await PhysicalMutationFileGuard.RestoreEntryIfOwnedAsync(mutation.Path, mutation.Before, mutation.After,
                        (path, token) => ReadAuthorizedAsync(workspaceId, path, token),
                        (path, expectedBytes, bytes, token) => WriteAsync(workspaceId, path, expectedBytes, bytes, physicalOperation, token), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (lease is not null)
            {
                try
                {
                    if (failures.Count == 1) await lease.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                    else recoveryAuthority.RetainRecovery(physicalOperation);
                }
                catch (Exception retentionFailure) { failures.Add(retentionFailure); }
            }
            if (failures.Count > 1)
            {
                primaryFailure = new AggregateException("Physical persistence failed and restoring original files also failed.", failures);
                throw primaryFailure;
            }
            if (failure is OperationCanceledException or IPublicMessageException) throw;
            if (failure is not IOException and not UnauthorizedAccessException and not InvalidOperationException) throw;
            _logger.LogWarning(failure, "Failed to persist changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Physical persistence ownership cleanup failed.", primaryFailure, lease, desiredGraph); }
    }

    private Task WriteAsync(string workspaceId, string path, byte[]? expected, byte[]? bytes,
        PhysicalMutationRecovery operation, CancellationToken ct)
    {
        var authority = _workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("Physical writes require workspace-owned recovery authority.");
        return _fileSystem.TransitionAsync(path, expected, bytes, operation,
            authority, ct);
    }
    private Task<byte[]?> ReadAuthorizedAsync(string workspaceId, string path, CancellationToken ct)
        => ReadAuthorizedAsync(workspaceId, path, _fileSystem.ReadAllBytesAsync, ct);

    private Task<FileContentDigest?> ReadDigestAuthorizedAsync(string workspaceId, string path, CancellationToken ct)
        => ReadAuthorizedAsync(workspaceId, path, _fileSystem.ReadDigestAsync, ct);

    private async Task<T?> ReadAuthorizedAsync<T>(string workspaceId, string path, Func<string, CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        var authority = _workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("Physical reads require workspace-owned recovery authority.");
        await authority.AuthorizePathsAsync(workspaceId, [path], ct).ConfigureAwait(false);
        WorkspaceManager.EnsurePhysicalPath(path);
        return await FileSnapshotCapture.ReadIfPresentAsync(path, read, ct).ConfigureAwait(false);
    }

    internal static bool ProjectReferenceTargetsPath(string? include, string projectDirectory, string targetProjectPath) =>
        SolutionFileMutationPlanner.ProjectReferenceTargetsPath(include, projectDirectory, targetProjectPath);
}

internal interface IDocumentSetFileSystem
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct);
    Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct);
    Task TransitionAsync(string path, byte[]? expected, byte[]? desired, PhysicalMutationRecovery operation,
        IWorkspaceRecoveryAuthority authority, CancellationToken ct);
}

internal sealed class PhysicalDocumentSetFileSystem : IDocumentSetFileSystem
{
    public static PhysicalDocumentSetFileSystem Instance { get; } = new();
    private PhysicalDocumentSetFileSystem() { }
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => PhysicalInputReader.ReadAllBytesAsync(path, ct);
    public Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct) => PhysicalInputReader.ReadDigestAsync(path, ct);
    public Task TransitionAsync(string path, byte[]? expected, byte[]? desired, PhysicalMutationRecovery operation,
        IWorkspaceRecoveryAuthority authority, CancellationToken ct)
        => operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
}
