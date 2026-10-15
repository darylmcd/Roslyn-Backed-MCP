using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Observes tokens through the store contract without depending on its storage representation.</summary>
internal sealed class TrackingPreviewStore(IPreviewStore inner) : IPreviewStore
{
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);

    internal IReadOnlyList<string> GetRedeemableTokens(string workspaceId) => _tokens
        .Where(pair => pair.Value == workspaceId && inner.PeekWorkspaceId(pair.Key) == workspaceId)
        .Select(pair => pair.Key).ToArray();

    private string Track(string workspaceId, string token)
    {
        Prune();
        _tokens[token] = workspaceId;
        return token;
    }

    private void Prune()
    {
        foreach (var token in _tokens.Keys)
            if (inner.PeekWorkspaceId(token) is null) _tokens.TryRemove(token, out _);
    }

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description) =>
        Track(workspaceId, inner.Store(workspaceId, modifiedSolution, workspaceVersion, description));

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated) =>
        Track(workspaceId, inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated));

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated, PreviewKind kind) =>
        Track(workspaceId, inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated, kind));

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes) =>
        Track(workspaceId, inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes));

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes, PreviewKind kind) =>
        Track(workspaceId, inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes, kind));

    public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => inner.Retrieve(token);

    public void Invalidate(string token)
    {
        inner.Invalidate(token);
        _tokens.TryRemove(token, out _);
    }

    public void InvalidateAll(string? workspaceId = null)
    {
        inner.InvalidateAll(workspaceId);
        Prune();
    }

    public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion)
    {
        inner.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion);
        Prune();
    }

    public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
    public IReadOnlyList<string>? PeekChangedPaths(string token) => inner.PeekChangedPaths(token);
    public PreviewKind PeekKind(string token) => inner.PeekKind(token);
}
