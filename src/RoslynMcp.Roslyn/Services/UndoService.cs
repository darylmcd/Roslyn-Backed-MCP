using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
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
    private readonly ConcurrentDictionary<string, List<UndoSnapshot>> _history = new(StringComparer.Ordinal);
    private readonly ILogger<UndoService> _logger;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IChangeTracker? _changeTracker;

    public UndoService(
        ILogger<UndoService> logger,
        IWorkspaceManager workspaceManager,
        IChangeTracker? changeTracker = null)
    {
        _logger = logger;
        _workspaceManager = workspaceManager;
        _changeTracker = changeTracker;
        _workspaceManager.WorkspaceClosed += Clear;
        if (_changeTracker is not null)
        {
            _changeTracker.ChangeRecorded += OnChangeRecorded;
        }
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= Clear;
        if (_changeTracker is not null)
        {
            _changeTracker.ChangeRecorded -= OnChangeRecorded;
        }
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

        var preApplyVersion = _workspaceManager.GetCurrentVersion(workspaceId);
        _snapshots[workspaceId] = new UndoSnapshot(
            workspaceId,
            description,
            solution,
            fileSnapshots,
            preApplyVersion,
            DateTime.UtcNow,
            SequenceNumber: 0,
            AffectedFiles: Array.Empty<string>());
    }

    public UndoEntry? GetLastOperation(string workspaceId)
    {
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return null;

        return new UndoEntry(snapshot.WorkspaceId, snapshot.Description, snapshot.AppliedAtUtc);
    }

    public async Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        if (!_snapshots.TryRemove(workspaceId, out var snapshot))
            return false;

        var workspace = _workspaceManager;

        // Also drop the matching history entry so the LIFO and by-sequence pathways agree
        // on what's been reverted. The pending snapshot is identified by sequence number
        // (assigned when ChangeTracker recorded the apply); look up the history entry by
        // that key. When CommitPendingCapture has not yet run (apply succeeded but
        // ChangeRecorded handler has not fired), the history list is empty for this id —
        // skip silently.
        if (snapshot.SequenceNumber > 0 && _history.TryGetValue(workspaceId, out var historyList))
        {
            lock (historyList)
            {
                for (var i = historyList.Count - 1; i >= 0; i--)
                {
                    if (historyList[i].SequenceNumber == snapshot.SequenceNumber)
                    {
                        historyList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

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

    public async Task<RevertBySequenceResult> RevertBySequenceAsync(
        string workspaceId,
        int sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_history.TryGetValue(workspaceId, out var historyList))
        {
            return new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: null,
                AffectedFiles: Array.Empty<string>(),
                Reason: "unknown-sequence",
                BlockingSequences: null);
        }

        UndoSnapshot? target;
        List<UndoSnapshot> laterApplies;
        lock (historyList)
        {
            target = historyList.FirstOrDefault(s => s.SequenceNumber == sequenceNumber);
            laterApplies = target is null
                ? new List<UndoSnapshot>()
                : historyList
                    .Where(s => s.SequenceNumber > sequenceNumber)
                    .ToList();
        }

        if (target is null)
        {
            return new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: null,
                AffectedFiles: Array.Empty<string>(),
                Reason: "unknown-sequence",
                BlockingSequences: null);
        }

        // Conservative dependency check: if any later apply touches a file that the target
        // apply also touched, reverting the target would silently roll back part of those
        // later applies. Block the revert and surface the offending sequence numbers so the
        // caller can revert them first if desired.
        var targetFiles = new HashSet<string>(target.AffectedFiles, StringComparer.OrdinalIgnoreCase);
        var blocking = new List<int>();
        foreach (var later in laterApplies)
        {
            foreach (var file in later.AffectedFiles)
            {
                if (targetFiles.Contains(file))
                {
                    blocking.Add(later.SequenceNumber);
                    break;
                }
            }
        }

        if (blocking.Count > 0)
        {
            return new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: target.Description,
                AffectedFiles: target.AffectedFiles,
                Reason: "dependency-blocked",
                BlockingSequences: blocking);
        }

        // Apply the revert. Drop the history entry first so concurrent callers can't double-revert.
        lock (historyList)
        {
            historyList.RemoveAll(s => s.SequenceNumber == sequenceNumber);
        }

        // If this is also the pending LIFO snapshot, drop it from _snapshots too — keeps
        // GetLastOperation / RevertAsync consistent.
        if (_snapshots.TryGetValue(workspaceId, out var pending) && pending.SequenceNumber == sequenceNumber)
        {
            _snapshots.TryRemove(workspaceId, out _);
        }

        bool reverted;
        if (target.FileSnapshots is { Count: > 0 })
        {
            reverted = await RevertFromFileSnapshotsAsync(workspaceId, _workspaceManager, target, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            reverted = await RevertFromSolutionSnapshotAsync(workspaceId, _workspaceManager, target, cancellationToken).ConfigureAwait(false);
        }

        if (!reverted)
        {
            return new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: target.Description,
                AffectedFiles: target.AffectedFiles,
                Reason: "revert-failed",
                BlockingSequences: null);
        }

        return new RevertBySequenceResult(
            Reverted: true,
            RevertedOperation: target.Description,
            AffectedFiles: target.AffectedFiles,
            Reason: null,
            BlockingSequences: null);
    }

    public void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles)
    {
        if (!_snapshots.TryGetValue(workspaceId, out var pending))
        {
            // No pending snapshot — apply path skipped CaptureBeforeApply (e.g., a no-op tool
            // that records a change without producing a revertable diff). Nothing to commit.
            return;
        }

        var promoted = pending with { SequenceNumber = sequenceNumber, AffectedFiles = affectedFiles };
        _snapshots[workspaceId] = promoted;

        var historyList = _history.GetOrAdd(workspaceId, _ => new List<UndoSnapshot>());
        lock (historyList)
        {
            historyList.Add(promoted);
        }
    }

    private void OnChangeRecorded(ChangeRecordedEventArgs e)
    {
        CommitPendingCapture(e.WorkspaceId, e.SequenceNumber, e.AffectedFiles);
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
                // Restore the workspace version to the pre-apply value so that preview tokens
                // created before the reverted operation remain valid. The workspace state has
                // been restored to its pre-apply form, so the pre-apply version is correct.
                workspace.RestoreVersion(workspaceId, snapshot.PreApplyWorkspaceVersion);
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
        var diskRestores = new List<(string FilePath, string Text)>();

        // Phase 1 (primary): trust the Solution diff to tell us what changed.
        var (targetSolution, changedDocumentCount) = await CollectChangesFromSolutionDiffAsync(
            snapshot.PreApplySolution, currentSolution, diskRestores, cancellationToken).ConfigureAwait(false);

        // Phase 2 (safety net, revert-last-apply-disk-consistency): if the Solution diff
        // came up empty we cannot conclude "nothing changed" — MSBuildWorkspace may have
        // quietly dropped the apply while leaving disk out of sync. Walk every document
        // in the pre-apply snapshot, read the on-disk text, and restore any that drifted.
        if (changedDocumentCount == 0)
        {
            changedDocumentCount = await CollectChangesFromDiskWalkAsync(
                snapshot.PreApplySolution, diskRestores, cancellationToken).ConfigureAwait(false);
        }

        if (changedDocumentCount == 0)
        {
            _logger.LogInformation(
                "UndoService.RevertAsync: snapshot for '{WorkspaceId}' contained no document differences against the current solution or disk. " +
                "Treating revert as a no-op success.",
                workspaceId);
            return true;
        }

        // Phase 3: persist the revert to workspace state + disk, reload, and restore version.
        return await PersistRevertChangesAsync(
            workspaceId, workspace, snapshot, targetSolution, diskRestores, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase-1 helper for <see cref="RevertFromSolutionSnapshotAsync"/>. Walks
    /// <c>preApplySolution.GetChanges(currentSolution)</c> and, for every document the
    /// diff flags as changed, schedules a workspace text revert and queues a disk
    /// restore. Returns the updated target solution plus a count of documents touched.
    /// </summary>
    private static async Task<(Solution TargetSolution, int ChangedDocumentCount)> CollectChangesFromSolutionDiffAsync(
        Solution preApplySolution,
        Solution currentSolution,
        List<(string FilePath, string Text)> diskRestores,
        CancellationToken cancellationToken)
    {
        var targetSolution = currentSolution;
        var changedDocumentCount = 0;

        foreach (var projectChange in preApplySolution.GetChanges(currentSolution).GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = preApplySolution.GetDocument(docId);
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

        return (targetSolution, changedDocumentCount);
    }

    /// <summary>
    /// Phase-2 helper (safety net) for <see cref="RevertFromSolutionSnapshotAsync"/>.
    /// Invoked only when the Solution diff returned no changes. Walks every document in
    /// the pre-apply snapshot, reads the on-disk text, and queues a disk restore for any
    /// file whose content drifted. Read failures are debug-logged and skipped.
    /// </summary>
    private async Task<int> CollectChangesFromDiskWalkAsync(
        Solution preApplySolution,
        List<(string FilePath, string Text)> diskRestores,
        CancellationToken cancellationToken)
    {
        var changedDocumentCount = 0;

        foreach (var project in preApplySolution.Projects)
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

        return changedDocumentCount;
    }

    /// <summary>
    /// Phase-3 helper for <see cref="RevertFromSolutionSnapshotAsync"/>. Applies the
    /// reverted solution to the workspace, writes every queued disk restore, reloads
    /// the workspace if any disk write succeeded, and restores the pre-apply workspace
    /// version. Returns <c>diskOk &amp;&amp; (workspaceApplied || anyDiskWrite)</c> — i.e.
    /// success requires every queued disk write to succeed AND at least one of the two
    /// state pathways (workspace apply / disk write) to land.
    /// </summary>
    private async Task<bool> PersistRevertChangesAsync(
        string workspaceId,
        IWorkspaceManager workspace,
        UndoSnapshot snapshot,
        Solution targetSolution,
        IReadOnlyList<(string FilePath, string Text)> diskRestores,
        CancellationToken cancellationToken)
    {
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

        // Restore the workspace version to the pre-apply value so that preview tokens
        // created before the reverted operation remain valid. The workspace state has
        // been restored to its pre-apply form, so the pre-apply version is correct.
        workspace.RestoreVersion(workspaceId, snapshot.PreApplyWorkspaceVersion);

        return diskOk && (workspaceApplied || anyDiskWrite);
    }

    public void Clear(string workspaceId)
    {
        _snapshots.TryRemove(workspaceId, out _);
        _history.TryRemove(workspaceId, out _);
    }

    private sealed record UndoSnapshot(
        string WorkspaceId,
        string Description,
        Solution? PreApplySolution,
        IReadOnlyList<FileSnapshotDto>? FileSnapshots,
        int PreApplyWorkspaceVersion,
        DateTime AppliedAtUtc,
        int SequenceNumber,
        IReadOnlyList<string> AffectedFiles);
}
