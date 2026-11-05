using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Minimal <see cref="IWorkspaceManager"/> double for composite-apply tests: records whether
/// <see cref="ReloadAsync"/> ran and, when <see cref="AdmittedWorkspaceIds"/> is set, models the
/// real manager's unknown-session behaviour (<c>ContainsWorkspace</c> is false and every
/// session-scoped member throws the not-found exception) so a test can prove a refusal happened
/// before any write rather than after one.
/// </summary>
internal sealed class RecordingWorkspaceManager(Exception? reloadFailure = null) : IWorkspaceManager
{
    /// <summary>
    /// The only workspace ids this double admits. <see langword="null"/> (the default) admits every
    /// non-blank id, the historic permissive shape the single-host orchestrator tests rely on.
    /// </summary>
    public IReadOnlyCollection<string>? AdmittedWorkspaceIds { get; init; }

    public bool ReloadCalled { get; private set; }

    public event Action<string>? WorkspaceClosed { add { } remove { } }
    public event Action<string>? WorkspaceReloaded { add { } remove { } }

    public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();

    public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
    {
        ReloadCalled = true;
        if (reloadFailure is not null)
        {
            return Task.FromException<WorkspaceStatusDto>(reloadFailure);
        }

        return ContainsWorkspace(workspaceId)
            ? Task.FromResult(GetStatus(workspaceId))
            : Task.FromException<WorkspaceStatusDto>(((IWorkspaceManager)this).CreateWorkspaceNotFoundException(workspaceId));
    }

    public bool ContainsWorkspace(string workspaceId) =>
        AdmittedWorkspaceIds is null
            ? !string.IsNullOrWhiteSpace(workspaceId)
            : AdmittedWorkspaceIds.Contains(workspaceId, StringComparer.Ordinal);

    public bool IsStale(string workspaceId) => false;
    public bool Close(string workspaceId) => throw new NotSupportedException();
    public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];

    public WorkspaceStatusDto GetStatus(string workspaceId) =>
        ContainsWorkspace(workspaceId)
            ? new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\repo\\Sample.slnx",
                WorkspaceVersion: 1,
                SnapshotToken: "snapshot",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: [])
            : throw ((IWorkspaceManager)this).CreateWorkspaceNotFoundException(workspaceId);

    public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetStatus(workspaceId));
    public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
    public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();

    public int GetCurrentVersion(string workspaceId) =>
        ContainsWorkspace(workspaceId)
            ? 1
            : throw ((IWorkspaceManager)this).CreateWorkspaceNotFoundException(workspaceId);

    public void RestoreVersion(string workspaceId, int version) { }
    public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
    public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
    public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
}
