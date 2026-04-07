using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Retains the last pre-apply solution snapshot per workspace so that it can
/// be restored via <see cref="RevertAsync"/>.
///
/// FLAG-9A: <see cref="MSBuildWorkspace"/>'s <c>TryApplyChanges</c> does not
/// reliably flush document text to disk. Earlier versions of this service called
/// only <c>TryApplyChanges</c> and reported <c>reverted: true</c> even though the
/// disk file still contained the post-apply text. The current implementation
/// mirrors <c>EditService.PersistDocumentTextToDiskAsync</c> and
/// <c>RefactoringService.PersistChangedDocumentsFromSolutionAsync</c> by writing
/// each changed document's text directly to disk after the workspace mutation,
/// then asking the workspace to reload so the next call sees a coherent state.
/// </summary>
public sealed class UndoService : IUndoService
{
    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ILogger<UndoService> _logger;

    public UndoService(ILogger<UndoService> logger)
    {
        _logger = logger;
    }

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

        // MSBuildWorkspace.TryApplyChanges only accepts solutions derived from its current
        // solution. Rebuild the target state by replacing document texts in the workspace's
        // current solution with the pre-apply snapshot texts.
        var currentSolution = workspace.GetCurrentSolution(workspaceId);
        var targetSolution = currentSolution;

        // Track the (filePath, oldText) tuples we need to write to disk after the workspace
        // mutation succeeds. We collect these BEFORE applying so we can persist exactly the
        // documents we restored. The path comes from the CURRENT solution because the snapshot
        // and current may have differing project layouts.
        var diskRestores = new List<(string FilePath, string Text)>();
        var changedDocumentCount = 0;

        foreach (var projectChange in snapshot.PreApplySolution.GetChanges(currentSolution).GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = snapshot.PreApplySolution.GetDocument(docId);
                if (oldDoc is null) continue;

                var oldText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                targetSolution = targetSolution.WithDocumentText(docId, oldText);
                changedDocumentCount++;

                var currentDoc = currentSolution.GetDocument(docId);
                var path = currentDoc?.FilePath ?? oldDoc.FilePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    diskRestores.Add((path, oldText.ToString()));
                }
            }
        }

        if (changedDocumentCount == 0)
        {
            _logger.LogInformation(
                "UndoService.RevertAsync: snapshot for '{WorkspaceId}' contained no document differences against the current solution. " +
                "Treating revert as a no-op success.",
                workspaceId);
            return true;
        }

        var workspaceApplied = workspace.TryApplyChanges(workspaceId, targetSolution);
        if (!workspaceApplied)
        {
            _logger.LogWarning(
                "UndoService.RevertAsync: workspace.TryApplyChanges returned false for '{WorkspaceId}' (description: '{Description}'). " +
                "Disk restore will still proceed because EditService.ApplyTextEdits has the same MSBuildWorkspace bug pattern.",
                workspaceId,
                snapshot.Description);
        }

        // FLAG-9A: explicitly persist the restored text to disk. MSBuildWorkspace.TryApplyChanges
        // does not always flush — see EditService.PersistDocumentTextToDiskAsync for the same
        // workaround. Without this, revert_last_apply returns reverted=true while disk is unchanged.
        var diskOk = true;
        var anyDiskWrite = false;
        foreach (var (filePath, text) in diskRestores)
        {
            try
            {
                await File.WriteAllTextAsync(filePath, text, cancellationToken).ConfigureAwait(false);
                anyDiskWrite = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "UndoService.RevertAsync: failed to persist {FilePath} to disk", filePath);
                diskOk = false;
            }
        }

        // FLAG-9A: report success only if BOTH the workspace mutation succeeded AND every disk
        // write succeeded. If the workspace mutation failed but every disk write succeeded, we
        // still treat the revert as successful — the source files on disk now reflect the
        // pre-apply state, which is what the operator cares about. Mirror that with a workspace
        // reload so the in-memory model resyncs.
        if (anyDiskWrite)
        {
            try
            {
                await workspace.ReloadAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "UndoService.RevertAsync: workspace reload after disk restore failed for {WorkspaceId}", workspaceId);
            }
        }

        return diskOk && (workspaceApplied || anyDiskWrite);
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
