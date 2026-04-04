using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Retains the last pre-apply solution snapshot per workspace so that it can
/// be restored via <see cref="RevertAsync"/>.
/// </summary>
public sealed class UndoService : IUndoService
{
    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);

    public void CaptureBeforeApply(string workspaceId, string description, object preApplySolution)
    {
        if (preApplySolution is not Solution solution)
            throw new ArgumentException("preApplySolution must be a Roslyn Solution.", nameof(preApplySolution));

        _snapshots[workspaceId] = new UndoSnapshot(workspaceId, description, solution, DateTime.UtcNow);
    }

    public UndoEntry? GetLastOperation(string workspaceId)
    {
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return null;

        return new UndoEntry(snapshot.WorkspaceId, snapshot.Description, snapshot.AppliedAtUtc);
    }

    public async Task<bool> RevertAsync(string workspaceId, IWorkspaceManager workspace, CancellationToken cancellationToken = default)
    {
        if (!_snapshots.TryRemove(workspaceId, out var snapshot))
            return false;

        // MSBuildWorkspace.TryApplyChanges only accepts solutions derived from its
        // current solution. We rebuild the target state by replacing document texts
        // in the workspace's current solution with the pre-apply snapshot texts.
        var currentSolution = workspace.GetCurrentSolution(workspaceId);
        var targetSolution = currentSolution;

        foreach (var projectChange in snapshot.PreApplySolution.GetChanges(currentSolution).GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = snapshot.PreApplySolution.GetDocument(docId);
                if (oldDoc is null) continue;

                var oldText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                targetSolution = targetSolution.WithDocumentText(docId, oldText);
            }
        }

        return workspace.TryApplyChanges(workspaceId, targetSolution);
    }

    public void Clear(string workspaceId)
    {
        _snapshots.TryRemove(workspaceId, out _);
    }

    private sealed record UndoSnapshot(
        string WorkspaceId,
        string Description,
        Solution PreApplySolution,
        DateTime AppliedAtUtc);
}
