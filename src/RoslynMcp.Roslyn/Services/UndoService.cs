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
/// document text (with a disk walk of the pre-apply documents when <c>GetChanges</c> is empty, so
/// an empty diff no longer silently wins) and FAILS CLOSED, retaining the snapshot, when the apply
/// also added/removed documents or projects or changed references or options, which it cannot undo.
/// </summary>
public sealed class UndoService : IUndoService, IDisposable
{
    // Process-wide so an id never aliases across workspaces or service instances; 0 is UndoCaptureId.None.
    private static long _captureCounter;

    private readonly ConcurrentDictionary<string, UndoSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<UndoSnapshot>> _history = new(StringComparer.Ordinal);
    private readonly ILogger<UndoService> _logger;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IChangeTracker? _changeTracker;

    /// <summary>Test seam: runs immediately before <see cref="RevertBySequenceAsync"/> enters the critical section that resolves, dependency-checks and claims the target.</summary>
    internal Action? BeforeSequenceClaim { get; set; }

    /// <summary>Test seam: runs inside <see cref="CommitPendingCapture"/> after it read the matching pending slot and before it swaps the promoted snapshot in, i.e. the window a newer <see cref="CaptureBeforeApply"/> (which takes no lock) can land in.</summary>
    internal Action? BeforeCommitSwap { get; set; }

    /// <summary>Test seam: runs in <see cref="RevertAsync"/> after the pending slot is claimed and before the restore starts, i.e. the window a concurrent by-sequence revert of the same operation can land in.</summary>
    internal Func<Task>? AfterLifoClaim { get; set; }

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
        Solution? solution = null;
        if (preApplySolution is not null)
        {
            if (preApplySolution is not Solution typed)
                throw new InvalidOperationException("preApplySolution must be a Roslyn Solution or null.");
            solution = typed;
        }

        var preApplyVersion = _workspaceManager.GetCurrentVersion(workspaceId);
        var capture = new UndoCaptureId(Interlocked.Increment(ref _captureCounter));
        _snapshots[workspaceId] = new UndoSnapshot(
            workspaceId,
            description,
            solution,
            fileSnapshots,
            preApplyVersion,
            DateTime.UtcNow,
            SequenceNumber: 0,
            AffectedFiles: Array.Empty<string>(),
            capture);
        return capture;
    }

    public UndoEntry? GetLastOperation(string workspaceId)
    {
        if (!_snapshots.TryGetValue(workspaceId, out var snapshot))
            return null;

        return new UndoEntry(snapshot.WorkspaceId, snapshot.Description, snapshot.AppliedAtUtc);
    }

    public async Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        // Claim the pending slot (and its history entry) atomically under the per-workspace lock,
        // exactly as RevertBySequenceAsync claims its target, so a concurrent by-sequence revert of
        // the same operation is refused instead of restoring it a second time. A failed (disk I/O)
        // or cancelled restore UNDOES the claim so the SAME call stays retryable once the
        // obstruction clears (ApplyWithVerifyTool's rollback and cancellation-recovery legs).
        if (!TryClaimPending(workspaceId, out var claim))
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

        return true;
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
        lock (historyList)
        {
            claimedIndex = historyList.FindIndex(s => s.SequenceNumber == sequenceNumber);
            if (claimedIndex < 0)
            {
                // Unknown, or a competing claimant already took it: refuse without restoring.
                return new RevertBySequenceResult(
                    Reverted: false,
                    RevertedOperation: null,
                    AffectedFiles: Array.Empty<string>(),
                    Reason: "unknown-sequence",
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
            wasPending = ClaimUnderLock(workspaceId, historyList, claimedIndex, target);
        }

        var claim = new RevertClaim(target, historyList, claimedIndex, wasPending);
        bool reverted;
        try
        {
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

    /// <summary>A won claim on one undo snapshot: the history position it left (-1: none) and whether it was also the pending LIFO slot.</summary>
    private sealed record RevertClaim(UndoSnapshot Target, List<UndoSnapshot>? HistoryList, int HistoryIndex, bool WasPending);

    /// <summary>
    /// Claims the pending LIFO snapshot for <see cref="RevertAsync"/> under the per-workspace lock
    /// (the one <see cref="RevertBySequenceAsync"/> and <see cref="CommitPendingCapture"/> use). A
    /// committed slot (sequence &gt; 0) also gives up its history entry; a sequence-0 slot has none,
    /// so its claim is the compare-and-remove alone.
    /// </summary>
    private bool TryClaimPending(string workspaceId, out RevertClaim claim)
    {
        claim = null!;
        if (!_history.TryGetValue(workspaceId, out var historyList))
        {
            // No history list exists (unknown/closed workspace, or nothing committed yet): only an
            // uncommitted sequence-0 slot can be pending, it has no history entry to give up, and every
            // other writer to the slot (capture, commit) is a compare-and-swap on the dictionary itself,
            // so the compare-and-remove alone is the claim. Never allocate a list for an unknown id.
            if (!_snapshots.TryGetValue(workspaceId, out var uncommitted) || uncommitted.SequenceNumber != 0)
                return false;

            if (!_snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, uncommitted)))
                return false;

            claim = new RevertClaim(uncommitted, HistoryList: null, HistoryIndex: -1, WasPending: true);
            return true;
        }

        lock (historyList)
        {
            if (!_snapshots.TryGetValue(workspaceId, out var pending))
                return false;

            var index = -1;
            if (pending.SequenceNumber > 0)
            {
                index = historyList.FindIndex(s => s.SequenceNumber == pending.SequenceNumber);
                if (index < 0)
                    return false; // A by-sequence revert already claimed it.
            }

            // CaptureBeforeApply writes the slot without this lock: a lost compare-and-remove means a
            // newer capture replaced the slot, which must survive untouched.
            if (!_snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, pending)))
                return false;

            if (index >= 0)
                historyList.RemoveAt(index);

            claim = new RevertClaim(pending, historyList, index, WasPending: true);
            return true;
        }
    }

    /// <summary>
    /// Caller holds the <paramref name="historyList"/> lock. Drops the by-sequence target's history
    /// entry and compare-removes it from the pending slot. Returns whether the target was the pending
    /// slot, so <see cref="UnclaimRevert"/> restores exactly what was taken.
    /// </summary>
    private bool ClaimUnderLock(string workspaceId, List<UndoSnapshot> historyList, int historyIndex, UndoSnapshot target)
    {
        historyList.RemoveAt(historyIndex);
        return _snapshots.TryRemove(new KeyValuePair<string, UndoSnapshot>(workspaceId, target));
    }

    /// <summary>
    /// Undoes a claimed-but-failed (or cancelled) revert: reinserts the history entry at (or as close
    /// as bounds allow to) its original ordinal position so the dependency-ordered overlap check stays
    /// correct, and re-installs the target as the pending LIFO snapshot ONLY when it was the pending
    /// slot when claimed (<see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> won't overwrite a
    /// newer capture that raced in during the restore). Callers pass only a claim they won.
    /// </summary>
    private void UnclaimRevert(string workspaceId, RevertClaim claim)
    {
        if (claim.HistoryList is not { } historyList)
        {
            if (claim.WasPending)
                _snapshots.TryAdd(workspaceId, claim.Target);
            return;
        }

        lock (historyList)
        {
            if (claim.HistoryIndex >= 0)
            {
                // Re-derive the insert position with a bounds check — concurrent CommitPendingCapture
                // calls may have grown the list while the restore was in flight.
                historyList.Insert(Math.Min(claim.HistoryIndex, historyList.Count), claim.Target);
            }

            if (claim.WasPending)
                _snapshots.TryAdd(workspaceId, claim.Target);
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
        // promotion and the history append are one step relative to claims and other commits.
        var historyList = _history.GetOrAdd(workspaceId, _ => new List<UndoSnapshot>());
        lock (historyList)
        {
            // Promote only the pending (uncommitted) snapshot this operation captured. A mismatch means
            // a newer capture replaced it (stale id: the newer slot must survive), the slot was already
            // promoted, consumed by an undo, or cleared.
            if (!_snapshots.TryGetValue(workspaceId, out var pending)
                || pending.CaptureId != capture
                || pending.SequenceNumber != 0)
            {
                return;
            }

            var promoted = pending with { SequenceNumber = sequenceNumber, AffectedFiles = affectedFiles };
            BeforeCommitSwap?.Invoke();

            // CaptureBeforeApply writes the slot without this lock, so a newer capture can land after
            // the read above. Compare-and-swap against the exact snapshot we validated: a lost race
            // leaves the newer capture in the slot with its own undo and adds no history for this one.
            if (!_snapshots.TryUpdate(workspaceId, promoted, pending))
            {
                return;
            }

            historyList.Add(promoted);
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
                WorkspaceManager.EnsurePhysicalPath(file.FilePath);
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
            : new HashSet<string>(snapshot.AffectedFiles, FileSystemPath.Comparer);
        bool InScope(string? path) => scope is null || (path is not null && scope.Contains(path));

        var before = preApplySolution.Projects.ToDictionary(ProjectKey);
        var after = currentSolution.Projects.ToDictionary(ProjectKey);

        foreach (var (key, project) in before)
        {
            if (!after.ContainsKey(key) && (scope is null || InScope(project.FilePath) || project.Documents.Any(d => InScope(d.FilePath))))
                return $"project '{project.Name}' was removed";
        }

        foreach (var (key, project) in after)
        {
            if (!before.ContainsKey(key) && (scope is null || InScope(project.FilePath) || project.Documents.Any(d => InScope(d.FilePath))))
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

    private static string ProjectKey(Project project) => (project.FilePath ?? string.Empty) + "\n" + project.Name;

    private static HashSet<string> DocumentPaths(Project project)
    {
        var paths = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var document in project.Documents.Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments))
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
    /// diff flags as changed, schedules a workspace text revert and queues a disk
    /// restore. Returns the updated target solution plus a count of documents touched.
    /// </summary>
    private static async Task<(Solution TargetSolution, int ChangedDocumentCount)> CollectChangesFromSolutionDiffAsync(
        Solution preApplySolution,
        Solution currentSolution,
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

                var oldText = await oldDoc.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var currentDoc = currentSolution.GetDocument(docId);

                // undo-restore-source-kind: the reviewed pre-apply SourceCodeKind is part of what an
                // apply may have changed; restore it with the text (mirrors RefactoringService rebase).
                if (currentDoc is not null && currentDoc.SourceCodeKind != oldDoc.SourceCodeKind)
                {
                    targetSolution = targetSolution.WithDocumentSourceCodeKind(docId, oldDoc.SourceCodeKind);
                }

                targetSolution = targetSolution.WithDocumentText(docId, oldText);
                changedDocumentCount++;

                var path = currentDoc?.FilePath ?? oldDoc.FilePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    diskRestores.Add((path!, oldText.ToString(), oldText.Encoding));
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
        List<(string FilePath, string Text, Encoding? Encoding)> diskRestores,
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
            WorkspaceManager.EnsurePhysicalPath(filePath);
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
        IReadOnlyList<string> AffectedFiles,
        UndoCaptureId CaptureId);
}
