using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Retains explicit byte snapshots and operation-owned compensation authority.
/// Workspace writers serialize preparation, publication and restoration through their write gate.
/// </summary>
public sealed class UndoService : IUndoService, IDisposable
{
    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<UndoSnapshot>> _history = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, byte> _restoring = new();
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

    public async Task<IPreparedUndoOperation> PrepareBeforeApplyAsync(IWorkspaceMutationOperation physicalOperation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(physicalOperation);
        if (physicalOperation is not PhysicalMutationRecovery recovery)
            throw InvalidOperationErrors.Internal("Undo preparation requires an issued physical operation.");
        var recoveryAuthority = RequireRecoveryAuthority();
        await recoveryAuthority.EnsurePreparedOperationCurrentAsync(recovery, ct).ConfigureAwait(false);
        var workspaceId = recovery.WorkspaceId;
        _snapshots.TryGetValue(workspaceId, out var predecessor);
        var snapshot = new UndoSnapshot(workspaceId, recovery.Description, recovery.Files, DateTime.UtcNow,
            0, Array.Empty<string>(), recovery.OperationId, recovery.CapturedLayout);
        return new PreparedOperation(this, snapshot, predecessor, recoveryAuthority, recovery);
    }

    private sealed class PreparedOperation(UndoService owner, UndoSnapshot snapshot, UndoSnapshot? predecessor,
        IWorkspaceRecoveryAuthority recoveryAuthority, PhysicalMutationRecovery recovery) : IPreparedUndoOperation
    {
        private int _state;
        private Solution? _publishedSolution;
        private int _publishedVersion;

        public async Task PublishAsync(CancellationToken cancellationToken)
        {
            if (_state != 0) throw InvalidOperationErrors.Internal("Undo operation has already been published or restored.");
            try
            {
                await recoveryAuthority.EnsureOperationCurrentAsync(recovery, cancellationToken).ConfigureAwait(false);
                _publishedVersion = owner._workspaceManager.GetCurrentVersion(snapshot.WorkspaceId);
                _publishedSolution = owner._workspaceManager.GetCurrentSolution(snapshot.WorkspaceId);
                var bytes = new Dictionary<string, byte[]?>(FileSystemPath.Comparer);
                foreach (var file in snapshot.FileSnapshots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await recoveryAuthority.AuthorizePathsAsync(snapshot.WorkspaceId, [file.FilePath], cancellationToken).ConfigureAwait(false);
                    bytes[file.FilePath] = await FileSnapshotCapture.ReadIfPresentAsync(file.FilePath, PhysicalInputReader.ReadAllBytesAsync, cancellationToken).ConfigureAwait(false);
                }
                await recoveryAuthority.EnsureOperationCurrentAsync(recovery, cancellationToken).ConfigureAwait(false);
                var changed = snapshot.FileSnapshots.Where(file => !MatchesPreState(file, bytes[file.FilePath])).ToArray();
                if (changed.Length == 0) { _state = 2; return; }
                owner._snapshots[snapshot.WorkspaceId] = snapshot with
                {
                    FileSnapshots = changed
                };
                _state = 1;
            }
            catch (WorkspaceMutationOwnershipException) { throw; }
            catch (Exception publicationFailure)
            {
                var failures = new List<Exception> { publicationFailure };
                var restored = false;
                try { restored = await RestoreAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception compensationFailure)
                {
                    failures.Add(compensationFailure);
                    if (_state == 2) throw new AggregateException("Undo publication failed; compensation committed but resource cleanup also failed.", failures);
                }
                if (restored) throw;
                if (failures.Count == 1)
                    failures.Add(InvalidOperationErrors.Internal("Compensation failed after undo publication could not capture post-state."));
                // Recovery belongs to this failed operation, never the unrelated previous
                // operation. No inferred forward graph is trustworthy after partial failure.
                try { RetainRecovery(); }
                catch (Exception retentionFailure) { failures.Add(retentionFailure); }
                throw new AggregateException("Undo publication and compensation failed; operation recovery remains available through user undo.", failures);
            }
        }

        public void RetainRecovery()
        {
            recoveryAuthority.RetainRecovery(recovery);
            if (_state == 1) RestorePredecessor();
            _state = 3;
        }

        private void RestorePredecessor()
        {
            if (owner._snapshots.TryGetValue(snapshot.WorkspaceId, out var current) && current.OperationId == snapshot.OperationId)
            {
                if (predecessor is null) owner._snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(snapshot.WorkspaceId, current));
                else owner._snapshots.TryUpdate(snapshot.WorkspaceId, predecessor, current);
            }
            if (owner._history.TryGetValue(snapshot.WorkspaceId, out var history))
                lock (history) history.RemoveAll(item => item.OperationId == snapshot.OperationId);
        }

        private static bool MatchesPreState(FileSnapshotDto before, byte[]? after) =>
            before.OriginalText is null ? after is null
                : after is not null && (!before.OriginalBytes.IsDefault
                    ? before.OriginalBytes.AsSpan().SequenceEqual(after)
                    : before.OriginalText == CsprojSemanticEquality.CreateSnapshot(after).Content);

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            using var claim = owner.ClaimRestoration(snapshot.OperationId);
            if (claim is null) return false;
            if (_state == 2) throw InvalidOperationErrors.Internal("Undo operation was already restored.");
            if (_state == 3) throw new PublicInvalidOperationException("Post-state capture failed; recover this operation through user undo before further mutations.");
            if (_state == 1)
            {
                if (!owner._snapshots.TryGetValue(snapshot.WorkspaceId, out var current)
                    || current.OperationId != snapshot.OperationId
                    || owner._workspaceManager.GetCurrentVersion(snapshot.WorkspaceId) != _publishedVersion
                    || !ReferenceEquals(owner._workspaceManager.GetCurrentSolution(snapshot.WorkspaceId), _publishedSolution))
                    throw new PublicInvalidOperationException("The operation was superseded; compensation cannot overwrite later workspace changes.");
                await recoveryAuthority.EnsureOperationCurrentAsync(recovery, cancellationToken).ConfigureAwait(false);
            }
            if (!await owner.RevertFromFileSnapshotsAsync(snapshot.WorkspaceId,
                snapshot with { FileSnapshots = recovery.RecoveryFiles }, cancellationToken, compensate: true, recovery: null, ownedOperation: recovery,
                onRestored: () => { if (_state == 1) RestorePredecessor(); _state = 2; }).ConfigureAwait(false)) return false;
            return true;
        }
    }

    private IWorkspaceRecoveryAuthority RequireRecoveryAuthority() => _workspaceManager as IWorkspaceRecoveryAuthority
        ?? throw InvalidOperationErrors.Internal("Undo requires workspace-owned physical recovery authority.");

    public UndoEntry? GetLastOperation(string workspaceId)
    {
        if (RequireRecoveryAuthority().GetPendingRecovery(workspaceId) is { } recovery)
            return new UndoEntry(workspaceId, recovery.Description, recovery.CapturedAtUtc);
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return null;

        return new UndoEntry(snapshot.WorkspaceId, snapshot.Description, snapshot.AppliedAtUtc);
    }

    public async Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var recoveryAuthority = RequireRecoveryAuthority();
        if (recoveryAuthority.GetPendingRecovery(workspaceId) is { } recovery)
        {
            using var claim = ClaimRestoration(recovery.OperationId);
            if (claim is null) return false;
            var recoverySnapshot = new UndoSnapshot(workspaceId, recovery.Description,
                recovery.RecoveryFiles, recovery.CapturedAtUtc, 0, [], recovery.OperationId, recovery.CapturedLayout);
            return await RevertFromFileSnapshotsAsync(workspaceId, recoverySnapshot,
                cancellationToken, compensate: false, recovery, ownedOperation: null, onRestored: () => ConsumeOperation(workspaceId, recovery.OperationId)).ConfigureAwait(false);
        }
        // Peek — do NOT consume the snapshot until the restore confirms success. A failed
        // (disk I/O) or cancelled restore must leave the snapshot and its history entry in
        // place so the SAME call is retryable once the obstruction clears; consuming up front
        // permanently destroys the retry target that the compensating callers depend on
        // (ApplyWithVerifyTool's rollback and cancellation-recovery legs).
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return false;

        using var snapshotClaim = ClaimRestoration(snapshot.OperationId);
        if (snapshotClaim is null) return false;
        if (!_snapshots.TryGetValue(workspaceId, out var selected) || selected.OperationId != snapshot.OperationId) return false;
        return await RevertFromFileSnapshotsAsync(workspaceId,
            snapshot, cancellationToken, compensate: false, recovery: null, ownedOperation: null,
            onRestored: () => ConsumeSnapshot(workspaceId, snapshot)).ConfigureAwait(false);
    }

    private void ConsumeOperation(string workspaceId, Guid operationId)
    {
        if (_snapshots.TryGetValue(workspaceId, out var current) && current.OperationId == operationId)
            ConsumeSnapshot(workspaceId, current);
        if (_history.TryGetValue(workspaceId, out var history))
            lock (history) history.RemoveAll(snapshot => snapshot.OperationId == operationId);
    }

    private void ConsumeSnapshot(string workspaceId, UndoSnapshot snapshot)
    {
        // Restore succeeded — consume the snapshot exactly once. Use the atomic
        // compare-and-remove overload so a newer PrepareBeforeApplyAsync that landed while the
        // restore was in flight is not clobbered (only the exact snapshot we restored is removed).
        _snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, snapshot));

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
        var targetFiles = new HashSet<string>(target.AffectedFiles, FileSystemPath.Comparer);
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

        using var operationClaim = ClaimRestoration(target.OperationId);
        if (operationClaim is null)
            return new RevertBySequenceResult(false, target.Description, target.AffectedFiles, "revert-in-progress", null);

        RequireRecoveryAuthority().EnsureMutationAllowed(workspaceId, target.FileSnapshots.Select(file => file.FilePath).ToArray());

        // Claim the revert BEFORE running it so concurrent callers can't double-revert: remove
        // the target history entry (recording its ordinal position so a failed/cancelled restore
        // can reinsert it at the same spot) and, if it is also the pending LIFO snapshot, remove
        // that too. Unlike the previous unconditional drop-first, a failed restore now UNDOES this
        // claim (below) instead of permanently destroying the retry target and its dependency ordering.
        int claimedIndex;
        lock (historyList)
        {
            claimedIndex = historyList.FindIndex(s => s.SequenceNumber == sequenceNumber);
            if (claimedIndex < 0)
                return new RevertBySequenceResult(false, target.Description, target.AffectedFiles, "unknown-sequence", null);
            historyList.RemoveAt(claimedIndex);
        }

        // If this is also the pending LIFO snapshot, drop it from _snapshots too — keeps
        // GetLastOperation / RevertAsync consistent. Use the compare-and-remove overload so a
        // newer capture that raced in is not clobbered.
        if (_snapshots.TryGetValue(workspaceId, out var pending) && pending.SequenceNumber == sequenceNumber)
        {
            _snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, pending));
        }

        bool reverted;
        var restorationCommitted = false;
        try
        {
            reverted = await RevertFromFileSnapshotsAsync(workspaceId,
                target, cancellationToken, compensate: false, recovery: null, ownedOperation: null,
                onRestored: () => restorationCommitted = true).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Every exceptional restore failure retains the same retry target and ordering,
            // including aggregate failures reported by transactional file rollback.
            if (!restorationCommitted) RestoreSequenceClaim(workspaceId, historyList, claimedIndex, target);
            throw;
        }

        if (!reverted)
        {
            // Restore failed (disk I/O). Undo the claim so the target sequence remains retryable
            // and keeps its ordinal position in the dependency-ordered history for future overlap
            // checks, then surface the failure.
            RestoreSequenceClaim(workspaceId, historyList, claimedIndex, target);
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
    /// Undoes a claimed-but-failed (or cancelled) sequence revert: reinserts the target history
    /// entry at (or as close as bounds allow to) its original ordinal position so the
    /// dependency-ordered overlap check stays correct, and best-effort restores it as the pending
    /// LIFO snapshot (<see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> won't overwrite a
    /// newer capture that raced in during the restore). No-op when nothing was claimed
    /// (<paramref name="claimedIndex"/> &lt; 0, e.g. a concurrent caller already claimed the entry).
    /// </summary>
    private void RestoreSequenceClaim(
        string workspaceId,
        List<UndoSnapshot> historyList,
        int claimedIndex,
        UndoSnapshot target)
    {
        if (claimedIndex < 0)
            return;

        lock (historyList)
        {
            // Re-derive the insert position with a bounds check — concurrent CommitPendingCapture
            // calls may have grown the list while the restore was in flight.
            var insertIndex = Math.Min(claimedIndex, historyList.Count);
            historyList.Insert(insertIndex, target);
        }

        _snapshots.TryAdd(workspaceId, target);
    }

    public void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles)
    {
        if (RequireRecoveryAuthority().GetPendingRecovery(workspaceId) is not null) return;
        if (!_snapshots.TryGetValue(workspaceId, out var pending))
        {
            // No pending snapshot — apply path skipped PrepareBeforeApplyAsync (e.g., a no-op tool
            // that records a change without producing a revertable diff). Nothing to commit.
            return;
        }

        if (pending.SequenceNumber > 0) return;
        var promoted = pending with { SequenceNumber = sequenceNumber, AffectedFiles = affectedFiles.ToArray() };
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

    private IDisposable? ClaimRestoration(Guid operationIdentity) => _restoring.TryAdd(operationIdentity, 0)
        ? new RestorationClaim(_restoring, operationIdentity) : null;

    private sealed class RestorationClaim(ConcurrentDictionary<Guid, byte> claims, Guid identity) : IDisposable
    {
        public void Dispose() => claims.TryRemove(identity, out _);
    }

    private async Task<bool> RevertFromFileSnapshotsAsync(
        string workspaceId,
        UndoSnapshot snapshot,
        CancellationToken cancellationToken,
        bool compensate,
        PhysicalMutationRecovery? recovery,
        PhysicalMutationRecovery? ownedOperation,
        Action onRestored)
    {
        var files = snapshot.FileSnapshots;
        var recoveryAuthority = RequireRecoveryAuthority();
        var before = new Dictionary<string, byte[]?>(FileSystemPath.Comparer);
        var touched = new HashSet<string>(FileSystemPath.Comparer);
        var intendedWrites = new Dictionary<string, byte[]?>(FileSystemPath.Comparer);
        PhysicalMutationRecovery? mutation = ownedOperation;
        IWorkspaceMutationLease? lease = null;
        IPhysicalRestorationAuthority? physical = null;
        Exception? primaryFailure = null;
        var restorationCommitted = false;
        async Task<byte[]?> ReadAuthorizedAsync(string path, CancellationToken token)
        {
            await recoveryAuthority.AuthorizePathsAsync(workspaceId, [path], token).ConfigureAwait(false);
            var bytes = await FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, token).ConfigureAwait(false);
            if ((recovery ?? mutation) is { } issued)
                recoveryAuthority.AssertKnownFileRead(issued, bytes is null ? new FileSnapshotDto(path, null) : FileSnapshotDto.FromExistingBytes(path, bytes));
            return bytes;
        }
        Task WriteAuthorizedAsync(string path, byte[]? expected, byte[]? bytes, CancellationToken token)
            => (recovery ?? mutation ?? throw InvalidOperationErrors.Internal("An inverse transition requires issued recovery ownership."))
                .TransitionSourceAsync(path, expected, bytes,
                    PhysicalSourceTransitionMode.FreshReplacement, recoveryAuthority, null, token);
        try
        {
            if (recovery is null) recoveryAuthority.EnsureMutationAllowed(workspaceId, files.Select(file => file.FilePath).ToArray());
            else await recoveryAuthority.EnsureRecoveryCurrentAsync(recovery, cancellationToken).ConfigureAwait(false);
            await recoveryAuthority.AuthorizePathsAsync(workspaceId, files.Select(file => file.FilePath).ToArray(), cancellationToken).ConfigureAwait(false);
            foreach (var file in files)
            {
                before[file.FilePath] = await ReadAuthorizedAsync(file.FilePath, cancellationToken).ConfigureAwait(false);
            }
            if (recovery is null)
            {
                mutation ??= await recoveryAuthority.CaptureRecoveryAsync(workspaceId, "Recover failed undo", before.Select(pair => pair.Value is null
                    ? new FileSnapshotDto(pair.Key, null) : FileSnapshotDto.FromExistingBytes(pair.Key, pair.Value)).ToArray(), cancellationToken).ConfigureAwait(false);
                lease = await recoveryAuthority.BeginMutationAsync(mutation, cancellationToken).ConfigureAwait(false);
            }
            physical = await recoveryAuthority.StageRestorationAsync(recovery ?? mutation!, files, snapshot.CapturedLayout, cancellationToken).ConfigureAwait(false);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PhysicalMutationFileGuard.AssertMatchesAsync(file.FilePath, before[file.FilePath], ReadAuthorizedAsync, cancellationToken).ConfigureAwait(false);
                var bytes = file.OriginalText is null ? null : !file.OriginalBytes.IsDefault ? file.OriginalBytes.ToArray()
                    : SolutionFileMutationPlanner.Encode(file.OriginalText, SourceFileEncoding.FromBytes(before[file.FilePath]));
                intendedWrites[file.FilePath] = bytes;
                lease?.BeforeWrite(bytes is null ? new FileSnapshotDto(file.FilePath, null) : FileSnapshotDto.FromExistingBytes(file.FilePath, bytes));
                touched.Add(file.FilePath);
                await WriteAuthorizedAsync(file.FilePath, before[file.FilePath], bytes, cancellationToken).ConfigureAwait(false);
            }
            if (recovery is null)
            {
                await recoveryAuthority.CommitRestorationAsync(mutation!, physical.Expected, retainedRecovery: false, cancellationToken).ConfigureAwait(false);
                foreach (var (path, bytes) in intendedWrites)
                    await PhysicalMutationFileGuard.AssertMatchesAsync(path, bytes, ReadAuthorizedAsync, cancellationToken).ConfigureAwait(false);
                if (compensate && physical.PreservesOriginalIdentity) recoveryAuthority.RestoreCapturedVersion(mutation!);
                var restored = files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
                lease!.Complete(mutation!.Files.Select(file => restored.GetValueOrDefault(file.FilePath) ?? file).ToArray());
            }
            else
            {
                foreach (var (path, bytes) in intendedWrites)
                    await PhysicalMutationFileGuard.AssertMatchesAsync(path, bytes, ReadAuthorizedAsync, cancellationToken).ConfigureAwait(false);
                await recoveryAuthority.CommitRestorationAsync(recovery, physical.Expected, retainedRecovery: true, cancellationToken).ConfigureAwait(false);
            }
            restorationCommitted = true;
            return true;
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            var failures = new List<Exception> { failure };
            foreach (var (path, bytes) in before.Where(pair => touched.Contains(pair.Key)))
            {
                try
                {
                    await PhysicalMutationFileGuard.RestoreEntryIfOwnedAsync(path, bytes, intendedWrites[path],
                        ReadAuthorizedAsync, WriteAuthorizedAsync, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (lease is not null)
            {
                try
                {
                    if (failures.Count == 1) await lease.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                    else recoveryAuthority.RetainRecovery(mutation!);
                }
                catch (Exception retentionFailure) { failures.Add(retentionFailure); }
            }
            if (failures.Count > 1) throw new AggregateException("Undo failed and restoring its entry state also failed.", failures);
            if (failure is OperationCanceledException or IPublicMessageException) throw;
            if (failure is not IOException and not UnauthorizedAccessException and not InvalidOperationException) throw;
            _logger.LogWarning(failure, "Undo failed before publishing a loaded workspace; undo authority was retained for {WorkspaceId}", workspaceId);
            return false;
        }
        finally
        {
            // Consume the exact undo authority before releasing resources. A disposal
            // failure remains observable without making a committed inverse retryable.
            if (restorationCommitted)
            {
                try { onRestored(); }
                catch (Exception completionFailure)
                {
                    OwnedResourceCleanup.Dispose("Undo completion and resource cleanup failed.", completionFailure, physical, lease);
                    throw;
                }
            }
            OwnedResourceCleanup.Dispose("Undo resource cleanup failed.", primaryFailure, physical, lease);
        }
    }

    public void Clear(string workspaceId)
    {
        _snapshots.TryRemove(workspaceId, out _);
        _history.TryRemove(workspaceId, out _);
    }

    private sealed record UndoSnapshot(
        string WorkspaceId,
        string Description,
        IReadOnlyList<FileSnapshotDto> FileSnapshots,
        DateTime AppliedAtUtc,
        int SequenceNumber,
        IReadOnlyList<string> AffectedFiles,
        Guid OperationId,
        PhysicalGraphLayout? CapturedLayout);
}
