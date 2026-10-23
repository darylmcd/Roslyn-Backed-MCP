using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Minimal workspace-manager test double. Only the explicitly configured list and handlers are
/// supported; every unrelated operation fails loudly so interface growth cannot create a
/// permissive test path by accident.
/// </summary>
internal sealed class FailClosedWorkspaceManagerStub(params WorkspaceStatusDto[] workspaces) : IWorkspaceManager, IPhysicalSolutionCommitter, IWorkspaceRecoveryAuthority
{
    private WorkspaceStatusDto[] _workspaces = workspaces;

    internal static FailClosedWorkspaceManagerStub ForRecovery(
        IWorkspaceManager manager,
        Func<string, IReadOnlyList<string>, CancellationToken, Task> authorize,
        Func<PhysicalMutationRecovery, IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto>, PhysicalGraphLayout?, CancellationToken, Task<IPhysicalRestorationAuthority>> stage,
        Func<PhysicalMutationRecovery, PhysicalGraphExpectation, bool, CancellationToken, Task> commitRestoration)
    {
        var authority = (IWorkspaceRecoveryAuthority)manager;
        return new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = authorize,
            StageRestorationHandler = stage,
            CommitRestorationHandler = commitRestoration,
            EnsureMutationAllowedHandler = authority.EnsureMutationAllowed,
            BeginMutationHandler = authority.BeginMutationAsync,
            EnsurePreparedOperationCurrentHandler = authority.EnsurePreparedOperationCurrentAsync,
            EnsureOperationCurrentHandler = authority.EnsureOperationCurrentAsync,
            RestoreCapturedVersionHandler = authority.RestoreCapturedVersion,
            CommitMutationHandler = authority.CommitMutationAsync,
            CaptureRecoveryHandler = authority.CaptureRecoveryAsync,
            GetPendingRecoveryHandler = authority.GetPendingRecovery,
            RetainRecoveryHandler = authority.RetainRecovery,
            AssertKnownFileReadHandler = authority.AssertKnownFileRead,
            EnsureRecoveryCurrentHandler = authority.EnsureRecoveryCurrentAsync,
            AcquireSourceCustodyHandler = authority.AcquireSourceCustodyAsync,
            GetCurrentSolutionHandler = manager.GetCurrentSolution,
            GetCurrentVersionHandler = manager.GetCurrentVersion
        };
    }

    public Func<string, Solution?, CancellationToken, Task>? CommitPhysicalSolutionHandler { get; init; }
    Task IPhysicalSolutionCommitter.CommitPhysicalSolutionAsync(string workspaceId, Solution? expected, CancellationToken ct) =>
        CommitPhysicalSolutionHandler is { } handler ? handler(workspaceId, expected, ct) : throw Unsupported();

    public Action<string, IReadOnlyList<string>>? EnsureMutationAllowedHandler { get; init; }
    public Func<string, IReadOnlyList<string>, CancellationToken, Task>? AuthorizePathsHandler { get; init; }
    public Func<PhysicalMutationRecovery, CancellationToken, Task<IWorkspaceMutationLease>>? BeginMutationHandler { get; init; }
    public Func<PhysicalMutationRecovery, CancellationToken, Task>? EnsurePreparedOperationCurrentHandler { get; init; }
    public Func<PhysicalMutationRecovery, CancellationToken, Task>? EnsureOperationCurrentHandler { get; init; }
    public Action<PhysicalMutationRecovery>? RestoreCapturedVersionHandler { get; init; }
    public Func<PhysicalMutationRecovery, Solution?, CancellationToken, Task>? CommitMutationHandler { get; init; }
    public Func<PhysicalMutationRecovery, IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto>, PhysicalGraphLayout?, CancellationToken, Task<IPhysicalRestorationAuthority>>? StageRestorationHandler { get; init; }
    public Func<PhysicalMutationRecovery, PhysicalGraphExpectation, bool, CancellationToken, Task>? CommitRestorationHandler { get; init; }
    public Func<string, string, IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto>, CancellationToken, Task<PhysicalMutationRecovery>>? CaptureRecoveryHandler { get; init; }
    public Func<string, PhysicalMutationRecovery?>? GetPendingRecoveryHandler { get; init; }
    public Action<PhysicalMutationRecovery>? RetainRecoveryHandler { get; init; }
    public Func<PhysicalMutationRecovery, CancellationToken, Task>? EnsureRecoveryCurrentHandler { get; init; }
    public Action<PhysicalMutationRecovery, RoslynMcp.Core.Services.FileSnapshotDto>? AssertKnownFileReadHandler { get; init; }
    public Func<PhysicalMutationRecovery, string, PhysicalDirectoryScope, string, string, CancellationToken, Task<PhysicalSourceCustody>>? AcquireSourceCustodyHandler { get; init; }

    void IWorkspaceRecoveryAuthority.EnsureMutationAllowed(string workspaceId, IReadOnlyList<string> paths) =>
        (EnsureMutationAllowedHandler ?? throw Unsupported())(workspaceId, paths);
    Task IWorkspacePathAuthority.AuthorizePathsAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) =>
        (AuthorizePathsHandler ?? throw Unsupported())(workspaceId, paths, ct);
    Task<IWorkspaceMutationLease> IWorkspaceRecoveryAuthority.BeginMutationAsync(PhysicalMutationRecovery operation, CancellationToken ct) =>
        (BeginMutationHandler ?? throw Unsupported())(operation, ct);
    Task IWorkspaceRecoveryAuthority.EnsurePreparedOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct) =>
        (EnsurePreparedOperationCurrentHandler ?? throw Unsupported())(operation, ct);
    Task IWorkspaceRecoveryAuthority.EnsureOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct) =>
        (EnsureOperationCurrentHandler ?? throw Unsupported())(operation, ct);
    void IWorkspaceRecoveryAuthority.RestoreCapturedVersion(PhysicalMutationRecovery operation) =>
        (RestoreCapturedVersionHandler ?? throw Unsupported())(operation);
    Task IWorkspaceRecoveryAuthority.CommitMutationAsync(PhysicalMutationRecovery operation, Solution? expected, CancellationToken ct) =>
        (CommitMutationHandler ?? throw Unsupported())(operation, expected, ct);
    Task<IPhysicalRestorationAuthority> IWorkspaceRecoveryAuthority.StageRestorationAsync(PhysicalMutationRecovery operation, IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto> desiredFiles, PhysicalGraphLayout? priorLayout, CancellationToken ct) =>
        (StageRestorationHandler ?? throw Unsupported())(operation, desiredFiles, priorLayout, ct);
    Task<IPhysicalRestorationAuthority> IWorkspaceRecoveryAuthority.StageMutationAsync(PhysicalMutationRecovery operation,
        IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto> desiredFiles, CancellationToken ct) => throw Unsupported();
    Task IWorkspaceRecoveryAuthority.VerifyMutationNamespacesAsync(PhysicalMutationRecovery operation,
        IPhysicalRestorationAuthority restoration, MutationNamespaceTarget target, CancellationToken ct) => throw Unsupported();
    Task IWorkspaceRecoveryAuthority.CommitRestorationAsync(PhysicalMutationRecovery operation, PhysicalGraphExpectation expected, bool retainedRecovery, CancellationToken ct) =>
        (CommitRestorationHandler ?? throw Unsupported())(operation, expected, retainedRecovery, ct);
    Task<PhysicalMutationRecovery> IWorkspaceRecoveryAuthority.CaptureRecoveryAsync(string workspaceId, string description,
        IReadOnlyList<RoslynMcp.Core.Services.FileSnapshotDto> files, CancellationToken ct) => (CaptureRecoveryHandler ?? throw Unsupported())(workspaceId, description, files, ct);
    PhysicalMutationRecovery? IWorkspaceRecoveryAuthority.GetPendingRecovery(string workspaceId) =>
        (GetPendingRecoveryHandler ?? throw Unsupported())(workspaceId);
    void IWorkspaceRecoveryAuthority.RetainRecovery(PhysicalMutationRecovery recovery) =>
        (RetainRecoveryHandler ?? throw Unsupported())(recovery);
    void IWorkspaceRecoveryAuthority.AssertKnownFileRead(PhysicalMutationRecovery operation, RoslynMcp.Core.Services.FileSnapshotDto observed) =>
        (AssertKnownFileReadHandler ?? throw Unsupported())(operation, observed);
    Task IWorkspaceRecoveryAuthority.EnsureRecoveryCurrentAsync(PhysicalMutationRecovery recovery, CancellationToken ct) =>
        (EnsureRecoveryCurrentHandler ?? throw Unsupported())(recovery, ct);
    Task<PhysicalSourceCustody> IWorkspaceRecoveryAuthority.AcquireSourceCustodyAsync(PhysicalMutationRecovery operation,
        string sourcePath, PhysicalDirectoryScope sourceParent, string leafName, string temporaryName, CancellationToken ct) =>
        (AcquireSourceCustodyHandler ?? throw Unsupported())(operation, sourcePath, sourceParent, leafName, temporaryName, ct);


    public Func<string, WorkspaceStatusDto>? GetStatusHandler { get; init; }
    public Func<string, WorkspaceStatusDto>? GetStatusAsyncHandler { get; init; }
    public Func<string, Solution>? GetCurrentSolutionHandler { get; init; }
    public Func<string, int>? GetCurrentVersionHandler { get; init; }

    public Func<string, bool>? ContainsWorkspaceHandler { get; init; }
    public Func<string, bool>? IsStaleHandler { get; init; }
    public Func<string, CancellationToken, Task<WorkspaceStatusDto>>? ReloadHandler { get; init; }

    // Consumers may establish passive cache-invalidation subscriptions during construction.
    // Accept add/remove without publishing events; operational members still fail closed.
    public event Action<string>? WorkspaceClosed { add { } remove { } }
    public event Action<string>? WorkspaceReloaded { add { } remove { } }

    public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Volatile.Read(ref _workspaces);

    public void ReplaceWith(params WorkspaceStatusDto[] replacement) =>
        Volatile.Write(ref _workspaces, replacement);

    public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
        throw Unsupported();

    public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
        ReloadHandler is { } handler ? handler(workspaceId, ct) : throw Unsupported();

    public bool ContainsWorkspace(string workspaceId) =>
        ContainsWorkspaceHandler is { } handler ? handler(workspaceId) : throw Unsupported();
    public bool IsStale(string workspaceId) =>
        IsStaleHandler is { } handler ? handler(workspaceId) : throw Unsupported();
    public bool Close(string workspaceId) => throw Unsupported();
    public WorkspaceStatusDto GetStatus(string workspaceId) =>
        GetStatusHandler is { } handler ? handler(workspaceId) : throw Unsupported();

    public Task<WorkspaceStatusDto> GetStatusAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) =>
        GetStatusAsyncHandler is { } handler ? Task.FromResult(handler(workspaceId)) : throw Unsupported();

    public ProjectGraphDto GetProjectGraph(string workspaceId) => throw Unsupported();

    public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
        string workspaceId,
        string? projectName,
        CancellationToken ct) => throw Unsupported();

    public Task<string?> GetSourceTextAsync(
        string workspaceId,
        string filePath,
        CancellationToken ct) => throw Unsupported();

    public int GetCurrentVersion(string workspaceId) =>
        GetCurrentVersionHandler is { } handler ? handler(workspaceId) : throw Unsupported();
    public Task<System.Collections.Immutable.ImmutableArray<RoslynMcp.Roslyn.Contracts.AdmittedModuleImage>> ReadAdmittedImagesAsync(
        string workspaceId, Microsoft.CodeAnalysis.ProjectId projectId, Microsoft.CodeAnalysis.PortableExecutableReference reference, CancellationToken ct) =>
        throw new NotSupportedException();

    public Solution GetCurrentSolution(string workspaceId) =>
        GetCurrentSolutionHandler is { } handler ? handler(workspaceId) : throw Unsupported();
    public Project? GetProject(string workspaceId, string projectNameOrPath) => throw Unsupported();

    public void RestoreVersion(string workspaceId, int version) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This fail-closed workspace-manager test double only supports its workspace list and explicitly configured handlers.");
}
