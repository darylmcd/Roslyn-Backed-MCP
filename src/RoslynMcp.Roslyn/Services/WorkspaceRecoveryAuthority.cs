using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Owns recovery independently of optional undo services. Callers hold the workspace write gate.</summary>
internal interface IWorkspacePathAuthority
{
    Task AuthorizePathsAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct);
}

internal interface IWorkspaceRecoveryAuthority : IWorkspacePathAuthority
{
    void EnsureMutationAllowed(string workspaceId, IReadOnlyList<string> paths);
    Task<IWorkspaceMutationLease> BeginMutationAsync(PhysicalMutationRecovery operation, CancellationToken ct);
    Task EnsurePreparedOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct);
    Task EnsureOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct);
    void RestoreCapturedVersion(PhysicalMutationRecovery operation);
    Task CommitMutationAsync(PhysicalMutationRecovery operation, Solution? expected, CancellationToken ct);
    Task<IPhysicalRestorationAuthority> StageRestorationAsync(PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles, PhysicalGraphLayout? priorLayout, CancellationToken ct);
    Task<IPhysicalRestorationAuthority> StageMutationAsync(PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles, CancellationToken ct);
    Task VerifyMutationNamespacesAsync(PhysicalMutationRecovery operation, IPhysicalRestorationAuthority restoration, MutationNamespaceTarget target, CancellationToken ct);
    Task CommitRestorationAsync(PhysicalMutationRecovery operation, PhysicalGraphExpectation expected, bool retainedRecovery, CancellationToken ct);
    Task<PhysicalMutationRecovery> CaptureRecoveryAsync(string workspaceId, string description, IReadOnlyList<FileSnapshotDto> beforeFiles, CancellationToken ct);
    PhysicalMutationRecovery? GetPendingRecovery(string workspaceId);
    void RetainRecovery(PhysicalMutationRecovery recovery);
    Task EnsureRecoveryCurrentAsync(PhysicalMutationRecovery recovery, CancellationToken ct);
    Task<PhysicalSourceCustody> AcquireSourceCustodyAsync(PhysicalMutationRecovery operation, string sourcePath,
        PhysicalDirectoryScope sourceParent, string leafName, string temporaryName, CancellationToken ct);
    /// <summary>Validates the exact already-authorized read buffer against issued byte ownership; performs no I/O.</summary>
    void AssertKnownFileRead(PhysicalMutationRecovery operation, FileSnapshotDto observed);
}

/// <summary>An immutable, session-bound capability for one operation's captured pre-state.</summary>
internal interface IWorkspaceMutationLease : IDisposable
{
    void BeforeWrite(FileSnapshotDto intendedPost);
    void Complete(IReadOnlyList<FileSnapshotDto> intendedPostFiles);
    void CancelBeforeSourceTransition(IPhysicalRestorationAuthority verification);
    Task AbortAsync(CancellationToken ct);
}

internal sealed record PhysicalGraphExpectation(Solution Solution, Func<string, string> MapPath, IReadOnlySet<string> PathProperties,
    EvaluatedExecutionIdentity ExecutionIdentity, ImmutableArray<PhysicalInputIdentity> BuildInputs, InstalledPackageAuthority Packages,
    ImmutableArray<SdkExistenceInput> SdkInputs)
{
    internal async Task AssertBuildInputsAsync(string workspaceId, IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        SdkExistenceInput.AssertCurrent(SdkInputs, ct);
        await pathPolicy.ValidateAsync(workspaceId, Packages.PathsRequiringCurrentGrant(BuildInputs.Select(input => input.FilePath)), ct).ConfigureAwait(false);
        await Packages.AssertCurrentAsync((paths, token) => pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        await pathPolicy.ValidateAsync(workspaceId, Packages.PathsRequiringCurrentGrant(BuildInputs.Select(input => input.FilePath)), ct).ConfigureAwait(false);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(BuildInputs, ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(SdkInputs, ct);
    }
}

internal enum MutationNamespaceTarget { CapturedSource, Desired }

internal interface IPhysicalRestorationAuthority : IDisposable
{
    PhysicalGraphExpectation Expected { get; }
    bool PreservesOriginalIdentity { get; }
    Task AssertSourceBeforeEffectAsync(PhysicalMutationRecovery operation, IReadOnlyList<string> sourceCoordinates, CancellationToken ct);
}

/// <summary>Binds the exact forward source proof to the transition's final custody-grant boundary.</summary>
internal sealed class PhysicalSourceAdmission(PhysicalMutationRecovery operation, IPhysicalRestorationAuthority restoration)
{
    internal Task AssertCurrentAsync(IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
        => restoration.AssertSourceBeforeEffectAsync(operation, sourceCoordinates, ct);
}

internal sealed record PhysicalMutationRecovery(
    string WorkspaceId,
    string Description,
    Solution PreApplySolution,
    int PreApplyVersion,
    ImmutableArray<FileSnapshotDto> Files,
    object SessionIdentity) : IWorkspaceMutationOperation
{
    private readonly List<RoslynMcp.Roslyn.Helpers.PhysicalSourceTransition> _sourceTransitions = [];
    private readonly Dictionary<string, FileSnapshotDto> _completedSourceTransitions = new(RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer);
    private readonly HashSet<string> _unverifiedSourceNamespaces = new(RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer);
    internal bool HasAttemptedSourceTransition { get; private set; }
    internal bool HasCompletedSourceTransitions => _completedSourceTransitions.Count != 0;
    internal bool HasUnverifiedSourceNamespaces => _unverifiedSourceNamespaces.Count != 0;
    internal void RecordSourceNamespace(string path) => _unverifiedSourceNamespaces.Add(path);
    internal void SourceNamespacesVerified() => _unverifiedSourceNamespaces.Clear();
    internal FileSnapshotDto? CompletedSourceTransition(string path) => _completedSourceTransitions.GetValueOrDefault(path);
    internal bool HasRetainedSourceTransitions => _sourceTransitions.Count != 0;
    internal async Task TransitionSourceAsync(string path, byte[]? expected, byte[]? desired,
        RoslynMcp.Roslyn.Helpers.PhysicalSourceTransitionMode mode, IWorkspaceRecoveryAuthority authority,
        IPhysicalRestorationAuthority? sourceAdmission, CancellationToken ct)
    {
        // Even a refused or canceled attempt cannot later claim an unused write authority.
        HasAttemptedSourceTransition = true;
        if (!Files.Any(file => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(file.FilePath, path)))
            throw InvalidOperationErrors.Internal("The source transition is outside its issued recovery operation.");
        void AssertIssuedStates()
        {
            authority.AssertKnownFileRead(this, expected is null ? new FileSnapshotDto(path, null) : FileSnapshotDto.FromExistingBytes(path, expected));
            authority.AssertKnownFileRead(this, desired is null ? new FileSnapshotDto(path, null) : FileSnapshotDto.FromExistingBytes(path, desired));
        }
        AssertIssuedStates();
        await RoslynMcp.Roslyn.Helpers.PhysicalSourceTransition.ApplyAsync(path, expected, desired, mode,
            (parent, leaf, temporary, token) => authority.AcquireSourceCustodyAsync(this, path, parent, leaf, temporary, token), async (paths, token) =>
            {
                await authority.AuthorizePathsAsync(WorkspaceId, paths, token).ConfigureAwait(false);
                AssertIssuedStates();
            },
            transition => _sourceTransitions.Add(transition), RecordSourceNamespace,
            RoslynMcp.Roslyn.Helpers.PhysicalSourceTransition.OpenParent,
            sourceAdmission is null ? null : new PhysicalSourceAdmission(this, sourceAdmission), ct).ConfigureAwait(false);
        _completedSourceTransitions[path] = desired is null ? new FileSnapshotDto(path, null) : FileSnapshotDto.FromExistingBytes(path, desired);
    }
    internal async Task ResolveSourceTransitionsAsync(IWorkspaceRecoveryAuthority authority, CancellationToken ct)
    {
        foreach (PhysicalSourceTransition transition in _sourceTransitions.ToArray())
        {
            await transition.ResolveAsync((paths, token) => authority.AuthorizePathsAsync(WorkspaceId, paths, token), ct).ConfigureAwait(false);
            _sourceTransitions.Remove(transition);
        }
    }
    internal void ReleaseSourceTransitionHandles()
    {
        // Shutdown releases handles, never unknown retained filesystem content.
        var failures = new List<Exception>();
        foreach (PhysicalSourceTransition transition in _sourceTransitions)
        {
            try { transition.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (HasRetainedSourceTransitions)
        {
            var retained = new IOException("Unresolved source transition custody remains on disk; resolve the retained source entries before further mutation.");
            retained.Data["SourceTransitionCustody"] = _sourceTransitions.Select(transition => transition.CustodyPath).ToArray();
            failures.Add(retained);
        }
        if (failures.Count != 0) throw new AggregateException("Source transition custody was retained during workspace shutdown.", failures);
    }
    internal Guid OperationId { get; } = Guid.NewGuid();
    internal DateTime CapturedAtUtc { get; } = DateTime.UtcNow;
    internal ImmutableArray<FileSnapshotDto> RecoveryFiles { get; set; } = Files;
    internal required ImmutableArray<PhysicalInputIdentity> CapturedGraphFiles { get; init; }
    internal required PhysicalGraphLayout? CapturedLayout { get; init; }
}

internal enum PhysicalInputKind { Missing, File, Directory }

internal sealed record PhysicalInputIdentity(string FilePath, bool Exists, ImmutableArray<byte> ContentHash)
{
    // Existing files always carry SHA256 (32 bytes), so the existing-directory
    // empty hash is distinct from both missing inputs and empty regular files.
    internal PhysicalInputKind Kind => !Exists ? PhysicalInputKind.Missing : ContentHash.IsEmpty ? PhysicalInputKind.Directory : PhysicalInputKind.File;
    internal static ImmutableArray<PhysicalInputIdentity> MergeConsistent(IEnumerable<PhysicalInputIdentity> inputs)
    {
        var result = new Dictionary<string, PhysicalInputIdentity>(RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer);
        foreach (PhysicalInputIdentity input in inputs)
        {
            if (result.TryGetValue(input.FilePath, out PhysicalInputIdentity? prior)
                && (prior.Exists != input.Exists || !prior.ContentHash.AsSpan().SequenceEqual(input.ContentHash.AsSpan())))
            {
                throw new PublicInvalidOperationException("A physical input changed during preparation; retry against the current source.");
            }

            result[input.FilePath] = input;
        }
        return [.. result.Values];
    }

    internal static void AssertMatches(PhysicalInputIdentity expected, PhysicalInputIdentity actual)
    {
        if (expected.Exists != actual.Exists || !expected.ContentHash.AsSpan().SequenceEqual(actual.ContentHash.AsSpan()))
        {
            var failure = new PublicInvalidOperationException("Physical execution inputs changed after preparation; retry against the current source.");
            failure.Data["PhysicalInputPathSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(expected.FilePath)));
            failure.Data["ExpectedPhysicalInputKind"] = expected.Kind.ToString();
            failure.Data["ActualPhysicalInputKind"] = actual.Kind.ToString();
            throw failure;
        }
    }

    internal static async Task AssertCurrentAsync(IEnumerable<PhysicalInputIdentity> expected, CancellationToken ct)
    {
        foreach (PhysicalInputIdentity input in expected)
        {
            PhysicalInputIdentity actual = await CaptureAsync(input.FilePath, ct).ConfigureAwait(false);
            AssertMatches(input, actual);
        }
    }

    internal static Task<PhysicalInputIdentity> CaptureAsync(string path, CancellationToken ct)
        => RoslynMcp.Roslyn.Helpers.PhysicalInputReader.CaptureIdentityAsync(path, ct);
}

/// <summary>Ownership loss refuses compensation before any unrelated operation can be touched.</summary>
internal sealed class WorkspaceMutationOwnershipException(string message) : InvalidOperationException(message), IPublicMessageException
{
    public string PublicMessage => Message;
}
