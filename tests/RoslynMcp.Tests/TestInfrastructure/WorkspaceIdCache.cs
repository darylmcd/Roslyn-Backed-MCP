using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

internal sealed class WorkspaceIdCache
{
    private readonly Dictionary<string, string> _workspaceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public async Task<string> GetOrLoadAsync(IWorkspaceManager workspaceManager, string solutionPath, CancellationToken ct = default)
    {
        if (_workspaceIds.TryGetValue(solutionPath, out var cachedId))
        {
            return cachedId;
        }

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_workspaceIds.TryGetValue(solutionPath, out cachedId))
            {
                return cachedId;
            }

            var status = await workspaceManager.LoadAsync(solutionPath, ct).ConfigureAwait(false);
            _workspaceIds[solutionPath] = status.WorkspaceId;
            return status.WorkspaceId;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void Clear()
    {
        _workspaceIds.Clear();
    }
}