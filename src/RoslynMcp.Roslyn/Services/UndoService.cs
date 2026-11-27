using System.Collections.Concurrent;
using System.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
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
/// <see cref="CaptureBeforeApply"/> (required, never defaulted), and <see cref="RevertAsync"/> restores those files
/// directly from disk — no <c>GetChanges</c> dependency. <c>RefactoringService</c>
/// captures physical source and project before-images for document or reference changes.
/// Callers without file snapshots use a text-only solution-snapshot restore: it rewrites changed
/// document text and only the documents the apply owns: a committed snapshot owns exactly its
/// <see cref="UndoSnapshot.AffectedFiles"/> (a later file-disjoint sibling apply's edits survive), an
/// uncommitted one is the single pending apply and owns its own solution diff. A committed snapshot
/// whose own files show no diff additionally walks those files on disk (an empty diff no longer
/// silently wins). It reports failure, retaining the snapshot, when a committed snapshot restored
/// nothing, and FAILS CLOSED when the apply also added/removed documents or projects or changed
/// references or options, which it cannot undo.
/// </summary>
public sealed class UndoService : IUndoService, IDisposable
{
    // Process-wide so an id never aliases across workspaces or service instances; 0 is UndoCaptureId.None.
    private static long _captureCounter;

    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WorkspaceUndoHistory> _history = new(StringComparer.Ordinal);

    // Registry gate: orders Clear against every path that creates or enters a workspace's history or
    // writes its pending slot. Held only for dictionary work (no await, no I/O). Lock order is fixed:
    // _registrySync, then a WorkspaceUndoHistory lock; code holding a history lock never takes this gate.
    private readonly object _registrySync = new();
    private readonly ILogger<UndoService> _logger;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IChangeTracker? _changeTracker;

    /// <summary>Test seam: runs immediately before <see cref="RevertBySequenceAsync"/> enters the critical section that resolves, dependency-checks and claims the target.</summary>
    internal Action? BeforeSequenceClaim { get; set; }

    /// <summary>Test seam: runs inside <see cref="CommitPendingCapture"/> after it read the matching pending slot and before it swaps the promoted snapshot in, i.e. the window a newer <see cref="CaptureBeforeApply"/> (which writes the slot under the registry gate, never under this history lock) can land in.</summary>
    internal Action? BeforeCommitSwap { get; set; }

    /// <summary>Test seam: runs in <see cref="RevertAsync"/> after the pending slot is claimed and before the restore starts, i.e. the window a concurrent by-sequence revert of the same operation can land in.</summary>
    internal Func<Task>? AfterLifoClaim { get; set; }

    /// <summary>Test seam: runs in <see cref="RevertBySequenceAsync"/> after the target is claimed and before the restore starts, i.e. the window a workspace close (<see cref="Clear"/>) can land in.</summary>
    internal Func<Task>? AfterSequenceClaim { get; set; }

    /// <summary>Test seam: runs in the claim and commit paths after the registry-gate section (slot peek plus history lookup/creation) and before the history lock is taken, i.e. the window <see cref="Clear"/> can land in.</summary>
    internal Action? AfterRegistryPeek { get; set; }

    /// <summary>Test seam: number of workspaces that own a history list. Production code must not read it.</summary>
    internal int TrackedHistoryWorkspaceCount => _history.Count;

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

    public UndoCaptureId CaptureBeforeApply(
        string workspaceId,
        string description,
        object? preApplySolution,
        IReadOnlyList<FileSnapshotDto>? fileSnapshots)
    {
        var prepared = Prepare(workspaceId, description, preApplySolution, fileSnapshots);
        var capture = Publish(prepared, fileSnapshots, UndoRecoveryMode.Full);
        if (capture.IsNone)
            throw new KeyNotFoundException($"Workspace '{workspaceId}' was closed while its undo snapshot was being captured.");
        return capture;
    }

    public PreparedUndoCapture Prepare(
        string workspaceId,
        string description,
        object? preApplySolution,
        IReadOnlyList<FileSnapshotDto>? fileSnapshots)
    {
        if (preApplySolution is not null && preApplySolution is not Solution)
            throw InvalidOperationErrors.Internal("preApplySolution must be a Roslyn Solution or null.");

        return new PreparedUndoCapture(workspaceId, description, preApplySolution, fileSnapshots,
            _workspaceManager.GetCurrentVersion(workspaceId), DateTime.UtcNow);
    }

    public UndoCaptureId Publish(
        PreparedUndoCapture prepared,
        IReadOnlyList<FileSnapshotDto>? ownedSnapshots,
        UndoRecoveryMode recoveryMode)
    {
        if (recoveryMode is UndoRecoveryMode.OwnedFiles or UndoRecoveryMode.OwnedFilesWithForeignChanges
            && ownedSnapshots is not { Count: > 0 })
        {
            throw InvalidOperationErrors.Internal("File recovery requires owned file snapshots.");
        }

        lock (_registrySync)
        {
            if (!_workspaceManager.ContainsWorkspace(prepared.WorkspaceId))
                return UndoCaptureId.None;

            var capture = new UndoCaptureId(Interlocked.Increment(ref _captureCounter));
            _snapshots[prepared.WorkspaceId] = new UndoSnapshot(
                prepared.WorkspaceId,
                prepared.Description,
                recoveryMode == UndoRecoveryMode.Full ? (Solution?)prepared.PreApplySolution : null,
                ownedSnapshots,
                prepared.PreApplyWorkspaceVersion,
                prepared.CapturedAtUtc,
                SequenceNumber: 0,
                AffectedFiles: Array.Empty<string>(),
                capture,
                recoveryMode);
            return capture;
        }
    }

    public UndoEntry? GetLastOperation(string workspaceId)
    {
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return null;

        return new UndoEntry(snapshot.WorkspaceId, snapshot.Description, snapshot.AppliedAtUtc);
    }

    public Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        RevertPendingAsync(workspaceId, expectedCapture: null, cancellationToken);

    public Task<bool> RevertCaptureAsync(string workspaceId, UndoCaptureId capture, CancellationToken cancellationToken) =>
        capture.IsNone ? Task.FromResult(false) : RevertPendingAsync(workspaceId, capture, cancellationToken);

    private async Task<bool> RevertPendingAsync(string workspaceId, UndoCaptureId? expectedCapture, CancellationToken cancellationToken)
    {
        // Claim the pending slot (and its history entry) atomically under the per-workspace lock,
        // exactly as RevertBySequenceAsync claims its target, so a concurrent by-sequence revert of
        // the same operation is refused instead of restoring it a second time. A failed (disk I/O)
        // or cancelled restore UNDOES the claim so the SAME call stays retryable once the
        // obstruction clears (ApplyWithVerifyTool's rollback and cancellation-recovery legs).
        if (!TryClaimPending(workspaceId, expectedCapture, out var claim))
            return false;

        bool reverted;
        try
        {
            if (AfterLifoClaim is not null)
                await AfterLifoClaim().ConfigureAwait(false);

            // Fast path: the caller provided an explicit file-snapshot list. Restore those
            // files directly — authoritative, independent of the workspace solution state.
            // Legacy (else) path: solution-based restore with a snapshot-wide walk so
            // empty-GetChanges no longer silently wins.
            reverted = await RevertSnapshotAsync(workspaceId, claim.Target, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            UnclaimRevert(workspaceId, claim);
            throw;
        }

        if (!reverted)
        {
            UnclaimRevert(workspaceId, claim);
            return false;
        }

        CompleteClaim(claim);
        return true;
    }

    public async Task<RevertBySequenceResult> RevertBySequenceAsync(
        string workspaceId,
        int sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        // No history object means nothing was ever committed or reverted for this workspace (or it was
        // cleared together with its consumed-sequence record): a pure lookup, never an allocation.
        if (!_history.TryGetValue(workspaceId, out var history))
        {
            return new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: null,
                AffectedFiles: Array.Empty<string>(),
                Reason: "unknown-sequence",
                BlockingSequences: null);
        }

        // Resolve the target, run the conservative dependency check and claim the revert inside ONE
        // critical section. Any split between the check and the claim leaves a window in which a
        // later overlapping apply (invalidating the check) or a competing claimant (taking the same
        // entry) lands in between. Everything here is in-memory and cheap, so the lock is not held
        // across the restore. Claiming removes the target history entry (recording its ordinal
        // position so a failed/cancelled restore can reinsert it at the same spot); a failed restore
        // UNDOES this claim (below) instead of destroying the retry target and its dependency ordering.
        BeforeSequenceClaim?.Invoke();
        UndoSnapshot target;
        int claimedIndex;
        bool wasPending;
        lock (history)
        {
            if (history.Cleared)
            {
                // The workspace closed after the lookup: its history (and consumed record) is gone.
                return new RevertBySequenceResult(
                    Reverted: false,
                    RevertedOperation: null,
                    AffectedFiles: Array.Empty<string>(),
                    Reason: "unknown-sequence",
                    BlockingSequences: null);
            }

            var historyList = history.Entries;
            claimedIndex = historyList.FindIndex(s => s.SequenceNumber == sequenceNumber);
            if (claimedIndex < 0)
            {
                // Already reverted (or being reverted by a competing claimant, which marks the
                // sequence consumed at claim time) versus never in this workspace's history.
                return new RevertBySequenceResult(
                    Reverted: false,
                    RevertedOperation: null,
                    AffectedFiles: Array.Empty<string>(),
                    Reason: history.ConsumedSequences.Contains(sequenceNumber) ? "already-reverted" : "unknown-sequence",
                    BlockingSequences: null);
            }

            target = historyList[claimedIndex];

            // Conservative dependency check: if any later apply touches a file that the target
            // apply also touched, reverting the target would silently roll back part of those
            // later applies. Block the revert and surface the offending sequence numbers so the
            // caller can revert them first if desired.
            var targetFiles = new HashSet<string>(target.AffectedFiles, FileSystemPath.Comparer);
            var blocking = new List<int>();
            foreach (var later in historyList)
            {
                if (later.SequenceNumber <= sequenceNumber)
                    continue;

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

            // Claim the history entry and, when the target is also the pending LIFO snapshot, the slot
            // too (compare-and-remove: a newer capture that raced in is not clobbered) — all under
            // this lock, so RevertAsync's claim cannot interleave.
            wasPending = ClaimUnderLock(workspaceId, history, claimedIndex, target);
        }

        var claim = new RevertClaim(target, history, claimedIndex, wasPending) { ConsumedSequence = sequenceNumber };
        bool reverted;
        try
        {
            if (AfterSequenceClaim is not null)
                await AfterSequenceClaim().ConfigureAwait(false);

            reverted = await RevertSnapshotAsync(workspaceId, target, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A cancelled or faulted restore must not destroy the retry target: undo the claim so the
            // same sequence stays retryable with its ordering preserved, then rethrow so the failure
            // still surfaces to the caller.
            UnclaimRevert(workspaceId, claim);
            throw;
        }

        if (!reverted)
        {
            // Restore failed (disk I/O). Undo the claim so the target sequence remains retryable
            // and keeps its ordinal position in the dependency-ordered history for future overlap
            // checks, then surface the failure.
            UnclaimRevert(workspaceId, claim);
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

    /// <summary>
    /// A won claim on one undo snapshot: the history position it left (-1: none), whether it was also the
    /// pending LIFO slot, the sequence it marked consumed (0: none), and, for a sequence-0 slot, the
    /// commit that raced the claim (applied by <see cref="UnclaimRevert"/>, dropped by <see cref="CompleteClaim"/>).
    /// Mutable fields are only touched under the <see cref="History"/> lock.
    /// </summary>
    private sealed class RevertClaim(UndoSnapshot target, WorkspaceUndoHistory history, int historyIndex, bool wasPending)
    {
        public UndoSnapshot Target { get; } = target;
        public WorkspaceUndoHistory History { get; } = history;
        public int HistoryIndex { get; } = historyIndex;
        public bool WasPending { get; } = wasPending;
        public int ConsumedSequence { get; init; }
        public (int Sequence, IReadOnlyList<string> AffectedFiles)? DeferredCommit { get; set; }
    }

    /// <summary>
    /// Claims the pending LIFO snapshot for <see cref="RevertAsync"/> under the per-workspace lock
    /// (the one <see cref="RevertBySequenceAsync"/> and <see cref="CommitPendingCapture"/> use). A
    /// committed slot (sequence &gt; 0) also gives up its history entry and marks its sequence consumed; a
    /// sequence-0 slot registers as in flight so a commit that races the claim is deferred, not lost.
    /// </summary>
    private bool TryClaimPending(string workspaceId, UndoCaptureId? expectedCapture, out RevertClaim claim)
    {
        claim = null!;

        // Peek of the live slot and lookup/creation of the history are ONE registry-gate section, the
        // section Clear's detach-and-remove also runs in: Clear-first leaves no slot to peek (so no
        // history is created for a closed workspace); peek-first registers the history Clear then flags.
        // An unknown workspace has no slot and never allocates a history.
        WorkspaceUndoHistory history;
        lock (_registrySync)
        {
            if (!_snapshots.ContainsKey(workspaceId))
                return false;

            history = _history.GetOrAdd(workspaceId, static _ => new WorkspaceUndoHistory());
        }

        AfterRegistryPeek?.Invoke();

        lock (history)
        {
            if (history.Cleared || !_snapshots.TryGetValue(workspaceId, out var pending)
                || (expectedCapture.HasValue && pending.CaptureId != expectedCapture.Value))
                return false;

            var index = -1;
            if (pending.SequenceNumber > 0)
            {
                index = history.Entries.FindIndex(s => s.SequenceNumber == pending.SequenceNumber);
                if (index < 0)
                    return false; // A by-sequence revert already claimed it.
            }

            // CaptureBeforeApply writes the slot under the registry gate, not this history lock: a lost compare-and-remove means a
            // newer capture replaced the slot, which must survive untouched.
            if (!_snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, pending)))
                return false;

            if (index >= 0)
                history.Entries.RemoveAt(index);

            if (pending.SequenceNumber > 0)
            {
                history.ConsumedSequences.Add(pending.SequenceNumber);
                claim = new RevertClaim(pending, history, index, wasPending: true) { ConsumedSequence = pending.SequenceNumber };
            }
            else
            {
                claim = new RevertClaim(pending, history, index, wasPending: true);
                history.InFlightPending[pending.CaptureId] = claim;
            }

            return true;
        }
    }

    /// <summary>
    /// Caller holds the <paramref name="history"/> lock. Drops the by-sequence target's history entry,
    /// marks its sequence consumed, and compare-removes it from the pending slot. Returns whether the
    /// target was the pending slot, so <see cref="UnclaimRevert"/> restores exactly what was taken.
    /// </summary>
    private bool ClaimUnderLock(string workspaceId, WorkspaceUndoHistory history, int historyIndex, UndoSnapshot target)
    {
        history.Entries.RemoveAt(historyIndex);
        history.ConsumedSequences.Add(target.SequenceNumber);
        return _snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, target));
    }

    /// <summary>
    /// Undoes a claimed-but-failed (or cancelled) revert, under the history lock and only while the
    /// workspace is still open (<see cref="WorkspaceUndoHistory.Cleared"/>: a closed workspace gets no
    /// revived slot or entry): reinserts the history entry at (or as close as bounds allow to) its
    /// original ordinal position so the dependency-ordered overlap check stays correct, un-marks the
    /// consumed sequence, and re-installs the target as the pending LIFO snapshot ONLY when it was the
    /// pending slot when claimed (<see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> won't overwrite a
    /// newer capture that raced in during the restore). A commit deferred while a sequence-0 claim was
    /// in flight is applied here: the slot returns as the promoted snapshot with its history entry (no
    /// entry when a newer capture holds the slot, as in <see cref="CommitPendingCapture"/>). Callers pass
    /// only a claim they won.
    /// </summary>
    private void UnclaimRevert(string workspaceId, RevertClaim claim)
    {
        var history = claim.History;
        lock (history)
        {
            if (claim.Target.SequenceNumber == 0)
                history.InFlightPending.Remove(claim.Target.CaptureId);

            if (history.Cleared)
                return;

            if (claim.ConsumedSequence > 0)
                history.ConsumedSequences.Remove(claim.ConsumedSequence);

            if (claim.DeferredCommit is { } deferred)
            {
                var promoted = claim.Target with { SequenceNumber = deferred.Sequence, AffectedFiles = deferred.AffectedFiles };
                if (_snapshots.TryAdd(workspaceId, promoted))
                {
                    var at = history.Entries.FindIndex(s => s.SequenceNumber > deferred.Sequence);
                    history.Entries.Insert(at < 0 ? history.Entries.Count : at, promoted);
                }

                return;
            }

            if (claim.HistoryIndex >= 0)
            {
                // Re-derive the insert position with a bounds check — concurrent CommitPendingCapture
                // calls may have grown the list while the restore was in flight.
                history.Entries.Insert(Math.Min(claim.HistoryIndex, history.Entries.Count), claim.Target);
            }

            if (claim.WasPending)
                _snapshots.TryAdd(workspaceId, claim.Target);
        }
    }

    /// <summary>
    /// A LIFO revert succeeded: the sequence-0 claim leaves the in-flight set and a commit that raced it
    /// leaves no history entry (the operation is reverted) but its sequence reads as already reverted.
    /// </summary>
    private static void CompleteClaim(RevertClaim claim)
    {
        var history = claim.History;
        lock (history)
        {
            if (claim.Target.SequenceNumber == 0)
                history.InFlightPending.Remove(claim.Target.CaptureId);

            if (claim.DeferredCommit is { } deferred && !history.Cleared)
                history.ConsumedSequences.Add(deferred.Sequence);
        }
    }

    public void CommitPendingCapture(
        string workspaceId,
        int sequenceNumber,
        IReadOnlyList<string> affectedFiles,
        UndoCaptureId capture)
    {
        if (capture.IsNone)
        {
            // The record owns no capture (a successful no-effect audit). Never adopt whatever the
            // slot holds: it belongs to an earlier completed operation or to a failed/cancelled apply.
            return;
        }

        // Same per-workspace lock RevertBySequenceAsync claims under, so the ownership check, the
        // promotion and the history append are one step relative to claims and other commits. The slot
        // peek and the history lookup/creation are ONE registry-gate section (the one Clear runs in): a
        // history is created only for a live pending slot this capture owns, so a stale or after-Clear
        // commit never allocates one. An existing history is also consulted for a sequence-0 revert in
        // flight on this capture (its commit is deferred to the claim, not lost).
        WorkspaceUndoHistory? history;
        lock (_registrySync)
        {
            if (_snapshots.TryGetValue(workspaceId, out var peek)
                && peek.CaptureId == capture
                && peek.SequenceNumber == 0)
            {
                history = _history.GetOrAdd(workspaceId, static _ => new WorkspaceUndoHistory());
            }
            else
            {
                _history.TryGetValue(workspaceId, out history);
            }
        }

        if (history is null)
            return;

        AfterRegistryPeek?.Invoke();

        lock (history)
        {
            if (history.Cleared)
                return;

            // Promote only the pending (uncommitted) snapshot this operation captured. A mismatch means
            // a newer capture replaced it (stale id: the newer slot must survive), the slot was already
            // promoted, consumed by an undo, or cleared.
            if (!_snapshots.TryGetValue(workspaceId, out var pending)
                || pending.CaptureId != capture
                || pending.SequenceNumber != 0)
            {
                // The slot may be claimed by a LIFO revert of THIS capture that is still restoring: hand
                // the commit to the claim so an unclaim keeps the history entry.
                if (history.InFlightPending.TryGetValue(capture, out var inFlight) && inFlight.DeferredCommit is null)
                    inFlight.DeferredCommit = (sequenceNumber, affectedFiles);

                return;
            }

            var promoted = pending with { SequenceNumber = sequenceNumber, AffectedFiles = affectedFiles };
            BeforeCommitSwap?.Invoke();

            // CaptureBeforeApply writes the slot under the registry gate, not this history lock, so a newer capture can land after
            // the read above. Compare-and-swap against the exact snapshot we validated: a lost race
            // leaves the newer capture in the slot with its own undo and adds no history for this one.
            if (!_snapshots.TryUpdate(workspaceId, promoted, pending))
            {
                return;
            }

            history.Entries.Add(promoted);
        }
    }

    private void OnChangeRecorded(ChangeRecordedEventArgs e)
    {
        CommitPendingCapture(e.WorkspaceId, e.SequenceNumber, e.AffectedFiles, e.Capture);
    }

    private Task<bool> RevertSnapshotAsync(
        string workspaceId,
        UndoSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.RecoveryMode == UndoRecoveryMode.ReloadOnly)
            return ReloadForRecoveryAsync(workspaceId, cancellationToken);

        // No solution plus an explicitly empty file capture owns no effects. Missing
        // captures and solution-backed empty lists retain their existing restore paths.
        if (snapshot.PreApplySolution is null
            && snapshot.FileSnapshots is { Count: 0 }
            && snapshot.AffectedFiles.Count == 0)
        {
            return Task.FromResult(true);
        }

        return snapshot.FileSnapshots is { Count: > 0 }
            ? RevertFromFileSnapshotsAsync(workspaceId, _workspaceManager, snapshot, cancellationToken)
            : RevertFromSolutionSnapshotAsync(workspaceId, _workspaceManager, snapshot, cancellationToken);
    }

    private async Task<bool> ReloadForRecoveryAsync(string workspaceId, CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceManager.ReloadAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogWarning(ex, "Undo recovery reload failed for {WorkspaceId}", workspaceId);
            return false;
        }
    }

    private async Task<bool> RevertFromFileSnapshotsAsync(
        string workspaceId,
        IWorkspaceManager workspace,
        UndoSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var diskOk = true;
        var restoredAny = false;

        try
        {
            foreach (var file in snapshot.FileSnapshots!)
            {
                WorkspacePhysicalPathGuard.EnsurePhysicalPath(file.FilePath);
            }
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("UndoService.RevertAsync (file-snapshot): rejected a filesystem-link target.");
            return false;
        }

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
                    if (!file.OriginalBytes.IsDefault)
                    {
                        await AtomicFileWriter.WriteAllBytesAsync(
                            file.FilePath,
                            file.OriginalBytes.ToArray(),
                            cancellationToken,
                            logger: _logger).ConfigureAwait(false);
                    }
                    else
                    {
                        await AtomicFileWriter.WriteAllTextAsync(
                            file.FilePath,
                            file.OriginalText,
                            cancellationToken,
                            logger: _logger).ConfigureAwait(false);
                    }
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

        if (snapshot.RecoveryMode != UndoRecoveryMode.Full)
        {
            if ((restoredAny || diskOk) && !await ReloadForRecoveryAsync(workspaceId, cancellationToken).ConfigureAwait(false))
                return false;

            if (diskOk && snapshot.RecoveryMode == UndoRecoveryMode.OwnedFiles)
                workspace.RestoreVersion(workspaceId, snapshot.PreApplyWorkspaceVersion);
            return diskOk;
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

        // The restore below rewrites document text only. Fail closed (snapshot retained by the
        // caller) rather than report success while added/removed documents or projects, reference
        // changes or option changes stay applied.
        var unrestorable = FindUnrestorableChange(snapshot, snapshot.PreApplySolution, currentSolution);
        if (unrestorable is not null)
        {
            _logger.LogWarning(
                "UndoService.RevertAsync: refusing the solution-snapshot revert for '{WorkspaceId}' (description: '{Description}'): {Change}. " +
                "Only document text can be restored from a solution snapshot; the snapshot is retained.",
                workspaceId,
                snapshot.Description,
                unrestorable);
            return false;
        }

        // composite-apply-undo-encoding-still-lossy: the queued restores carry the pre-apply
        // `SourceText.Encoding` alongside the text so `PersistRevertChangesAsync` can rewrite each
        // file with its original BOM/encoding instead of collapsing everything to UTF-8-no-BOM.
        // `Encoding` is null for in-memory-synthesized text; `SourceFileEncoding.FromSourceText`
        // handles that.
        var diskRestores = new List<(string FilePath, string Text, Encoding? Encoding)>();

        // Only documents this apply owns may be rewritten. Committed: its recorded AffectedFiles (even
        // an empty set: a committed solution-only apply that recorded no files owns no document text,
        // project-level changes were refused above). Uncommitted (null): the single pending apply, whose
        // own solution diff is its attribution.
        var ownedFiles = TextRestoreScope(snapshot);

        // Phase 1 (primary): trust the Solution diff to tell us what changed.
        var (targetSolution, changedDocumentCount) = await CollectChangesFromSolutionDiffAsync(
            snapshot.PreApplySolution, currentSolution, ownedFiles, diskRestores, cancellationToken).ConfigureAwait(false);

        // Phase 2 (safety net, revert-last-apply-disk-consistency): if the Solution diff of a committed
        // snapshot came up empty we cannot conclude "nothing changed" — MSBuildWorkspace may have
        // quietly dropped the apply while leaving disk out of sync. Walk the apply's own files in the
        // pre-apply snapshot, read the on-disk text, and restore any that drifted. An uncommitted
        // snapshot has no file attribution, so disk drift there cannot be told apart from a sibling's
        // or the user's edit and is never rewritten.
        if (changedDocumentCount == 0 && ownedFiles is not null)
        {
            changedDocumentCount = await CollectChangesFromDiskWalkAsync(
                snapshot.PreApplySolution, ownedFiles, diskRestores, cancellationToken).ConfigureAwait(false);
        }

        if (changedDocumentCount == 0)
        {
            if (ownedFiles is null)
            {
                _logger.LogInformation(
                    "UndoService.RevertAsync: uncommitted snapshot for '{WorkspaceId}' contained no document differences against the current solution. " +
                    "Nothing was applied to undo; treating revert as a no-op success.",
                    workspaceId);
                return true;
            }

            _logger.LogWarning(
                "UndoService.RevertAsync: committed snapshot for '{WorkspaceId}' (description: '{Description}') restored no document: none of its {Count} affected file(s) differs from the pre-apply state. " +
                "Reporting failure; the snapshot is retained.",
                workspaceId,
                snapshot.Description,
                snapshot.AffectedFiles.Count);
            return false;
        }

        // Phase 3: persist the revert to workspace state + disk, reload, and restore version.
        return await PersistRevertChangesAsync(
            workspaceId, workspace, snapshot, targetSolution, diskRestores, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a description of the first change between <paramref name="preApplySolution"/> and
    /// <paramref name="currentSolution"/> that a text-only solution-snapshot restore cannot undo (added
    /// or removed documents including additional/analyzer-config ones, added or removed projects,
    /// project/metadata/analyzer reference changes, parse/compilation option changes), or null.
    /// Projects are matched by project file path plus name, never by ProjectId: a reload builds a new
    /// workspace whose ids share nothing with a pre-reload snapshot, and identical projects must not
    /// read as removed+added. Documents are matched by path for the same reason.
    /// A snapshot with recorded <see cref="UndoSnapshot.AffectedFiles"/> only answers for changes that
    /// involve them (a document by its path; project-level changes by the project file), so a later
    /// file-disjoint sibling apply never blocks an earlier revert. A snapshot with NO file scope (an
    /// uncommitted one, or a committed solution-only one such as an option/reference change, which
    /// records no files) cannot attribute anything by file, so the whole diff is its own.
    /// </summary>
    private static string? FindUnrestorableChange(UndoSnapshot snapshot, Solution preApplySolution, Solution currentSolution)
    {
        var scope = snapshot.SequenceNumber == 0 || snapshot.AffectedFiles.Count == 0
            ? null
            : AffectedFileSet(snapshot);
        bool InScope(string? path) => scope is null || (path is not null && scope.Contains(path));

        // Projects sharing one identity key (e.g. in-memory projects with the same name and no file) cannot
        // be matched across the two solutions: refuse instead of guessing.
        foreach (var solution in new[] { preApplySolution, currentSolution })
        {
            var duplicate = solution.Projects.GroupBy(ProjectKey).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                return $"project '{duplicate.First().Name}' has an ambiguous identity (duplicate name and path)";
        }

        var before = preApplySolution.Projects.ToDictionary(ProjectKey);
        var after = currentSolution.Projects.ToDictionary(ProjectKey);

        foreach (var (key, project) in before)
        {
            if (!after.ContainsKey(key) && (scope is null || InScope(project.FilePath) || AllTextDocuments(project).Any(d => InScope(d.FilePath))))
                return $"project '{project.Name}' was removed";
        }

        foreach (var (key, project) in after)
        {
            if (!before.ContainsKey(key) && (scope is null || InScope(project.FilePath) || AllTextDocuments(project).Any(d => InScope(d.FilePath))))
                return $"project '{project.Name}' was added";
        }

        foreach (var (key, oldProject) in before)
        {
            if (!after.TryGetValue(key, out var newProject))
                continue;

            var name = newProject.Name;
            if (!DocumentPaths(oldProject).SetEquals(DocumentPaths(newProject)))
            {
                var added = DocumentPaths(newProject).Except(DocumentPaths(oldProject), FileSystemPath.Comparer);
                var removed = DocumentPaths(oldProject).Except(DocumentPaths(newProject), FileSystemPath.Comparer);
                if (added.Any(InScope))
                    return $"a document was added to project '{name}'";
                if (removed.Any(InScope))
                    return $"a document was removed from project '{name}'";
            }

            if (!InScope(newProject.FilePath) && !InScope(oldProject.FilePath))
                continue;

            if (!ReferenceKeys(oldProject, before).SetEquals(ReferenceKeys(newProject, after)))
                return $"references changed in '{name}'";
            if (!Equals(oldProject.ParseOptions, newProject.ParseOptions))
                return $"parse options changed in '{name}'";
            if (!Equals(NormalizeOptions(oldProject.CompilationOptions), NormalizeOptions(newProject.CompilationOptions)))
                return $"compilation options changed in '{name}'";
        }

        return null;
    }

    private static HashSet<string> AffectedFileSet(UndoSnapshot snapshot) =>
        new(snapshot.AffectedFiles, FileSystemPath.Comparer);

    /// <summary>
    /// The documents a solution-snapshot revert may rewrite. A committed snapshot (sequence &gt; 0)
    /// owns exactly its recorded <see cref="UndoSnapshot.AffectedFiles"/>, possibly none: unlike
    /// <see cref="FindUnrestorableChange"/>'s project-level scope, an empty list never widens to the
    /// whole diff, because a later sibling apply's edits would be reverted with it. An uncommitted
    /// snapshot (null) is the single pending apply (a newer capture supersedes the slot), so its own
    /// solution diff is its attribution and no file-level scope exists.
    /// </summary>
    private static HashSet<string>? TextRestoreScope(UndoSnapshot snapshot) =>
        snapshot.SequenceNumber == 0 ? null : AffectedFileSet(snapshot);

    private static string ProjectKey(Project project) => (project.FilePath ?? string.Empty) + "\n" + project.Name;

    private static HashSet<string> DocumentPaths(Project project)
    {
        var paths = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var document in AllTextDocuments(project))
        {
            paths.Add(document.FilePath ?? "<in-memory>:" + document.Name);
        }

        return paths;
    }

    /// <summary>Project, metadata and analyzer references as workspace-independent strings (project references by target project key).</summary>
    private static HashSet<string> ReferenceKeys(Project project, Dictionary<string, Project> solutionProjects)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in project.ProjectReferences)
        {
            var target = solutionProjects.Values.FirstOrDefault(p => p.Id == reference.ProjectId);
            keys.Add("project:" + (target is null ? reference.ProjectId.ToString() : ProjectKey(target)));
        }

        foreach (var reference in project.MetadataReferences)
            keys.Add("metadata:" + reference.Display);
        foreach (var reference in project.AnalyzerReferences)
            keys.Add("analyzer:" + (reference.FullPath ?? reference.Display));
        return keys;
    }

    /// <summary>Drops per-workspace resolver/provider instances, which differ across loads of identical projects.</summary>
    private static CompilationOptions? NormalizeOptions(CompilationOptions? options) => options
        ?.WithMetadataReferenceResolver(null)
        .WithXmlReferenceResolver(null)
        .WithSourceReferenceResolver(null)
        .WithSyntaxTreeOptionsProvider(null)
        .WithStrongNameProvider(null);

    /// <summary>
    /// Phase-1 helper for <see cref="RevertFromSolutionSnapshotAsync"/>. Walks
    /// <c>preApplySolution.GetChanges(currentSolution)</c> and, for every document the
    /// diff flags as changed (documents, additional documents and analyzer-config documents) AND the
    /// apply owns (<paramref name="ownedFiles"/>; null = every changed one), schedules a workspace text
    /// revert (except analyzer-config documents, which only get the disk restore) and queues a disk
    /// restore. Returns the updated target solution plus a count of documents touched.
    /// </summary>
    private static async Task<(Solution TargetSolution, int ChangedDocumentCount)> CollectChangesFromSolutionDiffAsync(
        Solution preApplySolution,
        Solution currentSolution,
        HashSet<string>? ownedFiles,
        List<(string FilePath, string Text, Encoding? Encoding)> diskRestores,
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

                var currentDoc = currentSolution.GetDocument(docId);
                var oldText = await TryQueueDiskRestoreAsync(oldDoc, currentDoc, ownedFiles, diskRestores, cancellationToken).ConfigureAwait(false);
                if (oldText is null) continue;

                // undo-restore-source-kind: the reviewed pre-apply SourceCodeKind is part of what an
                // apply may have changed; restore it with the text (mirrors RefactoringService rebase).
                if (currentDoc is not null && currentDoc.SourceCodeKind != oldDoc.SourceCodeKind)
                {
                    targetSolution = targetSolution.WithDocumentSourceCodeKind(docId, oldDoc.SourceCodeKind);
                }

                targetSolution = targetSolution.WithDocumentText(docId, oldText);
                changedDocumentCount++;
            }

            // Additional documents are restored through the workspace as well as disk:
            // MSBuildWorkspace.TryApplyChanges supports them (it writes the file itself).
            foreach (var docId in projectChange.GetChangedAdditionalDocuments())
            {
                var oldDoc = preApplySolution.GetAdditionalDocument(docId);
                if (oldDoc is null) continue;

                var oldText = await TryQueueDiskRestoreAsync(
                    oldDoc, currentSolution.GetAdditionalDocument(docId), ownedFiles, diskRestores, cancellationToken).ConfigureAwait(false);
                if (oldText is null) continue;

                targetSolution = targetSolution.WithAdditionalDocumentText(docId, oldText);
                changedDocumentCount++;
            }

            // Analyzer-config documents are restored by the queued disk write alone (the reload in
            // PersistRevertChangesAsync re-reads them): MSBuildWorkspace.TryApplyChanges THROWS
            // NotSupportedException ("Changing analyzer config documents is not supported.") for a
            // changed one, so the text must never enter targetSolution.
            foreach (var docId in projectChange.GetChangedAnalyzerConfigDocuments())
            {
                var oldDoc = preApplySolution.GetAnalyzerConfigDocument(docId);
                if (oldDoc is null) continue;

                var oldText = await TryQueueDiskRestoreAsync(
                    oldDoc, currentSolution.GetAnalyzerConfigDocument(docId), ownedFiles, diskRestores, cancellationToken).ConfigureAwait(false);
                if (oldText is null) continue;

                changedDocumentCount++;
            }
        }

        return (targetSolution, changedDocumentCount);
    }

    /// <summary>
    /// Shared per-document step of <see cref="CollectChangesFromSolutionDiffAsync"/> for every
    /// <see cref="TextDocument"/> kind: returns null when the apply does not own the document
    /// (<paramref name="ownedFiles"/>; a later sibling apply's or unattributed edit is not this
    /// apply's to revert), else queues the pre-apply text for a disk restore and returns it.
    /// </summary>
    private static async Task<SourceText?> TryQueueDiskRestoreAsync(
        TextDocument oldDoc,
        TextDocument? currentDoc,
        HashSet<string>? ownedFiles,
        List<(string FilePath, string Text, Encoding? Encoding)> diskRestores,
        CancellationToken cancellationToken)
    {
        var path = currentDoc?.FilePath ?? oldDoc.FilePath;
        if (ownedFiles is not null && (path is null || !ownedFiles.Contains(path)))
            return null;

        var oldText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(path))
        {
            diskRestores.Add((path!, oldText.ToString(), oldText.Encoding));
        }

        return oldText;
    }

    /// <summary>Every text document of the project: source, additional and analyzer-config.</summary>
    private static IEnumerable<TextDocument> AllTextDocuments(Project project) =>
        project.Documents.Concat<TextDocument>(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments);

    /// <summary>
    /// Phase-2 helper (safety net) for <see cref="RevertFromSolutionSnapshotAsync"/>.
    /// Invoked only for a committed snapshot whose Solution diff returned no changes. Walks the
    /// pre-apply text documents (source, additional, analyzer-config) that are among the apply's own <paramref name="ownedFiles"/>, reads the
    /// on-disk text, and queues a disk restore for any file whose content drifted. Documents outside
    /// the apply's files are never touched. Read failures are debug-logged and skipped.
    /// </summary>
    private async Task<int> CollectChangesFromDiskWalkAsync(
        Solution preApplySolution,
        HashSet<string> ownedFiles,
        List<(string FilePath, string Text, Encoding? Encoding)> diskRestores,
        CancellationToken cancellationToken)
    {
        var changedDocumentCount = 0;

        foreach (var project in preApplySolution.Projects)
        {
            foreach (var oldDoc in AllTextDocuments(project))
            {
                if (oldDoc.FilePath is null) continue;
                if (!ownedFiles.Contains(oldDoc.FilePath)) continue;
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

                var oldSourceText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var oldText = oldSourceText.ToString();
                if (!string.Equals(currentDiskText, oldText, StringComparison.Ordinal))
                {
                    diskRestores.Add((oldDoc.FilePath, oldText, oldSourceText.Encoding));
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
        IReadOnlyList<(string FilePath, string Text, Encoding? Encoding)> diskRestores,
        CancellationToken cancellationToken)
    {
        foreach (var (filePath, _, _) in diskRestores)
        {
            WorkspacePhysicalPathGuard.EnsurePhysicalPath(filePath);
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
        foreach (var (filePath, text, encoding) in diskRestores)
        {
            try
            {
                // composite-apply-undo-encoding-still-lossy: restore with the pre-apply
                // `SourceText.Encoding` so a reverted file keeps its original BOM/encoding.
                await AtomicFileWriter.WriteAllTextAsync(
                    filePath,
                    text,
                    cancellationToken,
                    logger: _logger,
                    encoding: SourceFileEncoding.FromSourceText(encoding)).ConfigureAwait(false);
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
        // been restored to its pre-apply form, so the pre-apply version is correct — but only
        // when something was actually restored; lowering the version under live version-keyed
        // caches for a revert that did nothing would let them serve stale entries.
        if (workspaceApplied || anyDiskWrite)
        {
            workspace.RestoreVersion(workspaceId, snapshot.PreApplyWorkspaceVersion);
        }

        return diskOk && (workspaceApplied || anyDiskWrite);
    }

    public void Clear(string workspaceId)
    {
        // Detach the history, flag it, and drop the pending slot in ONE registry-gate section, the one
        // every claim/commit/capture peeks in. An in-flight revert's UnclaimRevert (history lock,
        // Cleared check) then either finished before this section (its reinstalled slot is removed
        // here) or finds the flag and revives nothing.
        lock (_registrySync)
        {
            if (_history.TryRemove(workspaceId, out var history))
            {
                lock (history)
                {
                    history.Cleared = true;
                }
            }

            _snapshots.TryRemove(workspaceId, out _);
        }
    }

    /// <summary>
    /// One workspace's committed undo history, also the lock object for every claim, commit and unclaim
    /// on it. Retained until <see cref="Clear"/>: <see cref="ConsumedSequences"/> grows by one int per
    /// reverted sequence for the workspace's lifetime (bounded by its apply count) and is released
    /// with the workspace.
    /// </summary>
    private sealed class WorkspaceUndoHistory
    {
        public List<UndoSnapshot> Entries { get; } = new();

        /// <summary>Sequences claimed by a revert (in flight or done): a repeat reports already-reverted, not unknown-sequence.</summary>
        public HashSet<int> ConsumedSequences { get; } = new();

        /// <summary>Sequence-0 pending slots claimed by <see cref="RevertAsync"/> and still restoring, by capture id.</summary>
        public Dictionary<UndoCaptureId, RevertClaim> InFlightPending { get; } = new();

        /// <summary>Set by <see cref="Clear"/> under the history lock; a cleared history accepts no claim, commit or unclaim.</summary>
        public bool Cleared { get; set; }
    }

    private sealed record UndoSnapshot(
        string WorkspaceId,
        string Description,
        Solution? PreApplySolution,
        IReadOnlyList<FileSnapshotDto>? FileSnapshots,
        int PreApplyWorkspaceVersion,
        DateTime AppliedAtUtc,
        int SequenceNumber,
        IReadOnlyList<string> AffectedFiles,
        UndoCaptureId CaptureId,
        UndoRecoveryMode RecoveryMode);
}
