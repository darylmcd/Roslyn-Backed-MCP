using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Minimal workspace-manager test double. Only the explicitly configured workspace list is
/// supported; every unrelated operation fails loudly so interface growth cannot create a
/// permissive test path by accident.
/// </summary>
internal sealed class FailClosedWorkspaceManagerStub(params WorkspaceStatusDto[] workspaces) : IWorkspaceManager
{
    private WorkspaceStatusDto[] _workspaces = workspaces;

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
        throw Unsupported();

    public bool ContainsWorkspace(string workspaceId) => throw Unsupported();
    public bool IsStale(string workspaceId) => throw Unsupported();
    public bool Close(string workspaceId) => throw Unsupported();
    public WorkspaceStatusDto GetStatus(string workspaceId) => throw Unsupported();

    public Task<WorkspaceStatusDto> GetStatusAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) => throw Unsupported();

    public ProjectGraphDto GetProjectGraph(string workspaceId) => throw Unsupported();

    public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
        string workspaceId,
        string? projectName,
        CancellationToken ct) => throw Unsupported();

    public Task<string?> GetSourceTextAsync(
        string workspaceId,
        string filePath,
        CancellationToken ct) => throw Unsupported();

    public int GetCurrentVersion(string workspaceId) => throw Unsupported();
    public Solution GetCurrentSolution(string workspaceId) => throw Unsupported();
    public Project? GetProject(string workspaceId, string projectNameOrPath) => throw Unsupported();
    public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw Unsupported();
    public void RestoreVersion(string workspaceId, int version) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This fail-closed workspace-manager test double only supports ListWorkspaces and ReplaceWith.");
}
