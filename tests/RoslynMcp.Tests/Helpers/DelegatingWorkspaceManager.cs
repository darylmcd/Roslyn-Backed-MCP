using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Pass-through <see cref="IWorkspaceManager"/>. Derive and override the virtual members a test
/// needs to observe or intercept; everything else forwards to <paramref name="inner"/>.
/// </summary>
internal abstract class DelegatingWorkspaceManager(IWorkspaceManager inner) : IWorkspaceManager
{
    public event Action<string>? WorkspaceClosed
    {
        add => inner.WorkspaceClosed += value;
        remove => inner.WorkspaceClosed -= value;
    }

    public event Action<string>? WorkspaceReloaded
    {
        add => inner.WorkspaceReloaded += value;
        remove => inner.WorkspaceReloaded -= value;
    }

    public virtual Solution GetCurrentSolution(string workspaceId) => inner.GetCurrentSolution(workspaceId);

    public virtual bool TryApplyChanges(string workspaceId, Solution newSolution) =>
        inner.TryApplyChanges(workspaceId, newSolution);

    public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
        inner.LoadAsync(path, evictPolicy, ct);

    public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
        inner.ReloadAsync(workspaceId, ct);

    public bool ContainsWorkspace(string workspaceId) => inner.ContainsWorkspace(workspaceId);

    public bool IsStale(string workspaceId) => inner.IsStale(workspaceId);

    public bool Close(string workspaceId) => inner.Close(workspaceId);

    public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => inner.ListWorkspaces();

    public IReadOnlyList<string> FindWorkspaceIdsContainingFile(string filePath) =>
        inner.FindWorkspaceIdsContainingFile(filePath);

    public WorkspaceStatusDto GetStatus(string workspaceId) => inner.GetStatus(workspaceId);

    public Task<WorkspaceStatusDto> GetStatusAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) => inner.GetStatusAsync(workspaceId, cancellationToken);

    public ProjectGraphDto GetProjectGraph(string workspaceId) => inner.GetProjectGraph(workspaceId);

    public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
        string workspaceId,
        string? projectName,
        CancellationToken ct) => inner.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, ct);

    public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
        inner.GetSourceTextAsync(workspaceId, filePath, ct);

    public int GetCurrentVersion(string workspaceId) => inner.GetCurrentVersion(workspaceId);

    public Project? GetProject(string workspaceId, string projectNameOrPath) =>
        inner.GetProject(workspaceId, projectNameOrPath);

    public void RestoreVersion(string workspaceId, int version) => inner.RestoreVersion(workspaceId, version);
}
