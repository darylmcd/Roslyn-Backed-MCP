using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

internal sealed class WorkspaceIdCache
{
    private readonly ConcurrentDictionary<string, string> _workspaceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    internal int Count => _workspaceIds.Count;

    public async Task<string> GetOrLoadAsync(WorkspaceManager workspaceManager, string solutionPath, CancellationToken ct = default)
    {
        if (_workspaceIds.TryGetValue(solutionPath, out var cachedId)
            && workspaceManager.ContainsWorkspace(cachedId))
        {
            return cachedId;
        }

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_workspaceIds.TryGetValue(solutionPath, out cachedId)
                && workspaceManager.ContainsWorkspace(cachedId))
            {
                return cachedId;
            }

            _workspaceIds.TryRemove(solutionPath, out _);
            var status = await workspaceManager.LoadAsync(solutionPath, EvictPolicy.Strict,
                OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>()), ct).ConfigureAwait(false);
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
