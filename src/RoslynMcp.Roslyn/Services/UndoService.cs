using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Retains the last pre-apply snapshot per workspace so that it can be restored via
/// <see cref="RevertAsync"/>.
///
/// FLAG-9A: <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/>'s
/// <c>TryApplyChanges</c> does not reliably flush document text to disk. Earlier
/// versions of this service relied solely on <c>Solution.GetChanges()</c> to decide
/// which documents to restore and wrote disk only for those — but when the post-apply
/// workspace solution had reverted silently (or matched the pre-apply snapshot for
/// unrelated reasons), <c>GetChanges</c> returned empty even though disk still held
/// the post-apply text, and <c>revert_last_apply</c> reported success without
/// touching disk. See backlog row <c>revert-last-apply-disk-consistency</c>.
///
/// The current implementation adds an authoritative fast path: callers that know
/// which files they are about to modify (<c>EditService</c>, <c>EditorConfigService</c>)
/// pass an explicit <see cref="FileSnapshotDto"/> list to
/// <see cref="CaptureBeforeApply"/>, and <see cref="RevertAsync"/> restores those files
/// directly from disk — no <c>GetChanges</c> dependency. Solution-based callers
/// (<c>RefactoringService</c>) keep the legacy path, which has been hardened with a
/// snapshot-wide document walk so empty-<c>GetChanges</c> no longer silently wins.
/// </summary>
public sealed class UndoService : IUndoService, IDisposable
{
    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ILogger<UndoService> _logger;
    private readonly IWorkspaceManager _workspaceManager;

    public UndoService(ILogger<UndoService> logger, IWorkspaceManager workspaceManager)
    {
        _logger = logger;
        _workspaceManager = workspaceManager;
        _workspaceManager.WorkspaceClosed += Clear;
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= Clear;
    }

    public void CaptureBeforeApply(
        string workspaceId,
        string description,
        object? preApplySolution,
        IReadOnlyList<FileSnapshotDto>? fileSnapshots = null)
    {
        Solution? solution = null;
        if (preApplySolution is not null)
        {
            if (preApplySolution is not Solution typed)
                throw new ArgumentException("preApplySolution must be a Roslyn Solution or null.", nameof(preApplySolution));
            solution = typed;
        }

        _snapshots[workspaceId] = new UndoSnapshot(
            workspaceId,
            description,
            solution,
            fileSnapshots,
            DateTime.UtcNow);
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

        // Fast path: the caller provided an explicit file-snapshot list. Restore those
        // files directly — authoritative, independent of the workspace solution state.
        if (snapshot.FileSnapshots is { Count: > 0 })
        {
            return await RevertFromFileSnapshotsAsync(workspaceId, workspace, snapshot, cancellationToken).ConfigureAwait(false);
        }

        // Legacy path: solution-based restore with a snapshot-wide walk so empty-GetChanges
        // no longer silently wins.
        return await RevertFromSolutionSnapshotAsync(workspaceId, workspace, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RevertFromFileSnapshotsAsync(
        string workspaceId,
        IWorkspaceManager workspace,
        UndoSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var diskOk = true;
        var restoredAny = false;

        foreach (var file in snapshot.FileSnapshots!)
        {
            try
            {
                if (file.OriginalText is null)
                {
                    // The file did not exist before the apply — delete it on revert.
                    if (File.Exists(file.FilePath))
                    {
                        File.Delete(file.FilePath);
                        restoredAny = true;
                    }
                }
                else
                {
                    var directory = Path.GetDirectoryName(file.FilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    await File.WriteAllTextAsync(file.FilePath, file.OriginalText, cancellationToken).ConfigureAwait(false);
                    restoredAny = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex,
                    "UndoService.RevertAsync (file-snapshot): failed to restore {FilePath}",
                    file.FilePath);
                diskOk = false;
            }
        }

        if (restoredAny)
        {
            try
            {
                await workspace.ReloadAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or KeyNotFoundException)
            {
                _logger.LogWarning(
                    ex,
                    "UndoService.RevertAsync (file-snapshot): workspace reload after disk restore failed for {WorkspaceId}",
                    workspaceId);
            }
        }

        return diskOk;
    }

    private async Task<bool> RevertFromSolutionSnapshotAsync(
        string workspaceId,
        IWorkspaceManager workspace,
        UndoSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.PreApplySolution is null)
        {
            _logger.LogWarning(
                "UndoService.RevertAsync: snapshot for '{WorkspaceId}' has neither a pre-apply Solution nor file snapshots. " +
                "Nothing to revert.",
                workspaceId);
            return false;
        }

        var currentSolution = workspace.GetCurrentSolution(workspaceId);
        var targetSolution = currentSolution;

        var diskRestores = new List<(string FilePath, string Text)>();
        var changedDocumentCount = 0;

        // Primary pass: trust the Solution diff to tell us what changed.
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
                    diskRestores.Add((path!, oldText.ToString()));
                }
            }
        }

        // Safety net (revert-last-apply-disk-consistency): if the Solution diff came up
        // empty we cannot conclude "nothing changed" — MSBuildWorkspace may have quietly
        // dropped the apply while leaving disk out of sync. Walk every document in the
        // pre-apply snapshot, read the on-disk text, and restore any that drifted.
        if (changedDocumentCount == 0)
        {
            foreach (var project in snapshot.PreApplySolution.Projects)
            {
                foreach (var oldDoc in project.Documents)
                {
                    if (oldDoc.FilePath is null) continue;
                    if (!File.Exists(oldDoc.FilePath)) continue;

                    string currentDiskText;
                    try
                    {
                        currentDiskText = await File.ReadAllTextAsync(oldDoc.FilePath, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogDebug(ex, "UndoService.RevertAsync: disk-walk read failed for {FilePath}", oldDoc.FilePath);
                        continue;
                    }

                    var oldText = (await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                    if (!string.Equals(currentDiskText, oldText, StringComparison.Ordinal))
                    {
                        diskRestores.Add((oldDoc.FilePath, oldText));
                        changedDocumentCount++;
                    }
                }
            }
        }

        if (changedDocumentCount == 0)
        {
            _logger.LogInformation(
                "UndoService.RevertAsync: snapshot for '{WorkspaceId}' contained no document differences against the current solution or disk. " +
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
        Solution? PreApplySolution,
        IReadOnlyList<FileSnapshotDto>? FileSnapshots,
        DateTime AppliedAtUtc);
}
