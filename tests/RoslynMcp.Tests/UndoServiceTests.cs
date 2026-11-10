using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies <see cref="IUndoService.RevertBySequenceAsync"/> — late-session revert of an
/// earlier apply identified by its <see cref="WorkspaceChangeDto.SequenceNumber"/>. Pairs
/// with <see cref="EditUndoIntegrationTests"/> (LIFO revert) and <see cref="ChangeTrackerTests"/>
/// (sequence-number assignment) — this fixture pins the dependency-block contract that is
/// unique to <c>revert_apply_by_sequence</c>.
/// </summary>
[TestClass]
public sealed class UndoServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RevertBySequence_NonOverlappingApplies_RestoresEarlierApply()
    {
        // Apply A (text edit to Dog.cs) then Apply B (text edit to Cat.cs). Revert A by
        // sequence number — B does not touch Dog.cs, so the dependency check must NOT
        // block the revert. Disk content for Dog.cs returns to pre-A state; Cat.cs keeps
        // its post-B content.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var catOriginal = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        // Apply A: insert a comment at the top of Dog.cs.
        var dogEdit = new TextEditDto(1, 1, 1, 1, "// apply A\n");
        var resultA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { dogEdit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultA.Success, "Apply A should succeed.");

        // Apply B: insert a comment at the top of Cat.cs.
        var catEdit = new TextEditDto(1, 1, 1, 1, "// apply B\n");
        var resultB = await EditService.ApplyTextEditsAsync(
            workspaceId, catFilePath, new[] { catEdit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultB.Success, "Apply B should succeed.");

        var changes = ChangeTracker.GetChanges(workspaceId);
        Assert.AreEqual(2, changes.Count, "Expected 2 recorded changes.");
        var sequenceA = changes[0].SequenceNumber;
        var sequenceB = changes[1].SequenceNumber;
        Assert.IsTrue(sequenceA < sequenceB, "Sequence numbers should increase per apply.");

        // Revert A by sequence — B's affected files do not overlap with A's, so this must succeed.
        var revertResult = await UndoService.RevertBySequenceAsync(
            workspaceId, sequenceA, CancellationToken.None);

        Assert.IsTrue(revertResult.Reverted,
            $"Expected revert of non-overlapping apply A to succeed; got reason={revertResult.Reason}.");
        Assert.IsNull(revertResult.Reason);
        Assert.IsNotNull(revertResult.RevertedOperation);

        // Dog.cs (apply A's target) is back to its original content; Cat.cs (apply B's target)
        // keeps its post-B content because B was NOT reverted.
        var dogAfterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(dogOriginal, dogAfterRevert,
            "Dog.cs should be restored to its pre-apply-A content after revert-by-sequence(A).");

        var catAfterRevert = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        StringAssert.Contains(catAfterRevert, "// apply B",
            "Cat.cs should retain apply B's edit — only apply A was reverted.");
        Assert.AreNotEqual(catOriginal, catAfterRevert,
            "Cat.cs should still be at its post-apply-B state.");
    }

    [TestMethod]
    public async Task RevertBySequence_LaterApplyOverlapsTargetFiles_DependencyBlocked()
    {
        // Apply A and Apply B both touch Dog.cs. Revert A by sequence — the conservative
        // dependency check must block the revert and surface B's sequence number as a
        // blocker so the caller can revert B first.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Apply A: insert a comment at the top of Dog.cs.
        var editA = new TextEditDto(1, 1, 1, 1, "// apply A\n");
        var resultA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { editA }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultA.Success, "Apply A should succeed.");

        // Apply B: insert another comment at the top of Dog.cs (now line 1 holds the apply-A
        // comment; insert above that). B touches the SAME file as A, so revert A must be blocked.
        var editB = new TextEditDto(1, 1, 1, 1, "// apply B\n");
        var resultB = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { editB }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultB.Success, "Apply B should succeed.");

        var changes = ChangeTracker.GetChanges(workspaceId);
        Assert.AreEqual(2, changes.Count, "Expected 2 recorded changes.");
        var sequenceA = changes[0].SequenceNumber;
        var sequenceB = changes[1].SequenceNumber;

        // Try to revert A — B touches Dog.cs which is also in A's affected set.
        var revertResult = await UndoService.RevertBySequenceAsync(
            workspaceId, sequenceA, CancellationToken.None);

        Assert.IsFalse(revertResult.Reverted, "Revert must be blocked when a later apply overlaps.");
        Assert.AreEqual("dependency-blocked", revertResult.Reason);
        Assert.IsNotNull(revertResult.BlockingSequences);
        CollectionAssert.Contains(
            revertResult.BlockingSequences!.ToList(),
            sequenceB,
            "Apply B's sequence number must appear in BlockingSequences because it touches Dog.cs.");

        // Disk for Dog.cs must remain at its post-apply-B state — no partial revert.
        var dogAfterBlock = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(dogAfterBlock, "// apply B",
            "Apply B's edit must remain on disk — dependency-blocked revert must not partially roll back.");
        StringAssert.Contains(dogAfterBlock, "// apply A",
            "Apply A's edit must also remain on disk — dependency-blocked revert must not roll it back partially.");
    }

    [TestMethod]
    public async Task RevertBySequence_UnknownSequence_ReturnsUnknownSequenceReason()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        // No applies have happened — every sequence number is unknown for this workspace.
        var revertResult = await UndoService.RevertBySequenceAsync(
            workspaceId, sequenceNumber: 9999, CancellationToken.None);

        Assert.IsFalse(revertResult.Reverted);
        Assert.AreEqual("unknown-sequence", revertResult.Reason);
        Assert.IsNull(revertResult.BlockingSequences);
    }

    [TestMethod]
    public async Task RevertAsync_RestoreFails_SnapshotSurvivesAndRetrySucceeds()
    {
        // A failed disk restore must NOT consume the pending snapshot — the compensating
        // caller (ApplyWithVerifyTool's rollback leg) needs the same snapshot to retry.
        // Force the file-snapshot restore to fail by replacing the target file with a
        // directory. Unlike read-only attributes, this obstruction is deterministic on
        // both Windows and Unix when the atomic writer replaces the target path.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        var edit = new TextEditDto(1, 1, 1, 1, "// apply\n");
        var applyResult = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Apply should succeed.");
        Assert.IsNotNull(UndoService.GetLastOperation(workspaceId), "Undo entry must exist after apply.");

        File.Delete(dogFilePath);
        Directory.CreateDirectory(dogFilePath);

        try
        {
            var firstRevert = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
            Assert.IsFalse(firstRevert, "Revert must report failure when the disk restore cannot write.");

            // The snapshot must have survived the failed restore.
            Assert.IsNotNull(UndoService.GetLastOperation(workspaceId),
                "A failed revert must leave the pending snapshot in place so it can be retried.");
        }
        finally
        {
            Directory.Delete(dogFilePath);
        }

        // Retry now that the file is writable again — the surviving snapshot must restore the original.
        var secondRevert = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(secondRevert, "Retry after clearing the obstruction must succeed.");

        var dogAfterRetry = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(dogOriginal, dogAfterRetry,
            "Retried revert must restore Dog.cs to its pre-apply content — proving the snapshot survived the first failure.");
        Assert.IsNull(UndoService.GetLastOperation(workspaceId),
            "The successful retry must finally consume the snapshot.");
    }

    [TestMethod]
    public async Task RevertAsync_Cancelled_SnapshotSurvivesAndRetrySucceeds()
    {
        // An already-cancelled token must not consume the snapshot: cancellation surfaces
        // (OperationCanceledException), but a follow-up revert with a live token still works.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        var edit = new TextEditDto(1, 1, 1, 1, "// apply\n");
        var applyResult = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Apply should succeed.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var threw = false;
        try
        {
            await UndoService.RevertAsync(workspaceId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "A revert driven by an already-cancelled token must surface cancellation.");
        Assert.IsNotNull(UndoService.GetLastOperation(workspaceId),
            "A cancelled revert must leave the pending snapshot in place so it can be retried.");

        var retry = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(retry, "Retry with a live token must succeed after the cancelled attempt.");

        var dogAfterRetry = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(dogOriginal, dogAfterRetry,
            "Retried revert must restore Dog.cs — proving the cancelled attempt did not consume the snapshot.");
    }

    [TestMethod]
    public async Task RevertAsync_HappyPath_ConsumesSnapshotExactlyOnce()
    {
        // A successful revert consumes the snapshot exactly once: the second call finds
        // nothing to revert and GetLastOperation returns null.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        var edit = new TextEditDto(1, 1, 1, 1, "// apply\n");
        var applyResult = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Apply should succeed.");

        var firstRevert = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(firstRevert, "First revert should succeed.");

        var dogAfterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(dogOriginal, dogAfterRevert, "Revert must restore the original content.");

        var secondRevert = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsFalse(secondRevert, "The snapshot must be consumed exactly once — a second revert finds nothing.");
        Assert.IsNull(UndoService.GetLastOperation(workspaceId),
            "GetLastOperation must return null after the snapshot has been consumed.");
    }

    [TestMethod]
    public async Task RevertBySequence_RestoreFails_SequenceRemainsRetryable()
    {
        // Apply A (Dog.cs) then Apply B (Cat.cs) — non-overlapping. Force the revert of A
        // to fail by replacing Dog.cs with a directory. The failed revert must NOT destroy A's history
        // entry: a follow-up RevertBySequenceAsync for the SAME sequence must succeed once the
        // obstruction clears, and B's edit must be untouched throughout (ordering preserved).
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var catOriginal = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        var dogEdit = new TextEditDto(1, 1, 1, 1, "// apply A\n");
        var resultA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { dogEdit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultA.Success, "Apply A should succeed.");

        var catEdit = new TextEditDto(1, 1, 1, 1, "// apply B\n");
        var resultB = await EditService.ApplyTextEditsAsync(
            workspaceId, catFilePath, new[] { catEdit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(resultB.Success, "Apply B should succeed.");

        var changes = ChangeTracker.GetChanges(workspaceId);
        Assert.AreEqual(2, changes.Count, "Expected 2 recorded changes.");
        var sequenceA = changes[0].SequenceNumber;

        File.Delete(dogFilePath);
        Directory.CreateDirectory(dogFilePath);

        try
        {
            var failedRevert = await UndoService.RevertBySequenceAsync(
                workspaceId, sequenceA, CancellationToken.None);
            Assert.IsFalse(failedRevert.Reverted, "Revert must report failure when the disk restore cannot write.");
            Assert.AreEqual("revert-failed", failedRevert.Reason);

            // B's edit must be untouched by the failed revert of A.
            var catAfterFailure = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
            StringAssert.Contains(catAfterFailure, "// apply B",
                "A failed revert of A must not touch B's file.");
        }
        finally
        {
            Directory.Delete(dogFilePath);
        }

        // The target sequence must still be retryable — its history entry survived the failure.
        var retryRevert = await UndoService.RevertBySequenceAsync(
            workspaceId, sequenceA, CancellationToken.None);
        Assert.IsTrue(retryRevert.Reverted,
            $"The target sequence must remain retryable after a failed restore; got reason={retryRevert.Reason}.");
        Assert.IsNull(retryRevert.Reason);

        var dogAfterRetry = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(dogOriginal, dogAfterRetry,
            "Retried revert-by-sequence must restore Dog.cs to its pre-apply-A content.");

        // B was never reverted — Cat.cs keeps apply B's edit; ordering was preserved across the failure.
        var catAfterRetry = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        StringAssert.Contains(catAfterRetry, "// apply B",
            "Cat.cs should retain apply B's edit — only apply A was reverted.");
        Assert.AreNotEqual(catOriginal, catAfterRetry, "Cat.cs should still be at its post-apply-B state.");
    }

    [TestMethod]
    public async Task RevertBySequence_ConcurrentClaimants_OnlyWinnerRestores()
    {
        // undo-sequence-claim-exclusivity: two callers undo the SAME sequence at the claim point
        // (both parked on a barrier, then released together). Exactly one may claim and restore;
        // the loser must refuse with unknown-sequence instead of restoring the same effects again.
        // Limit: the seam forces both callers to the claim together (race-forcing) but cannot force
        // the old two-lock interleaving, because the fixed code has no window to hook between check
        // and claim; the old-code red was observed with the seam placed between the old two locks.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        using var undo = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
        var captureRace = undo.CaptureBeforeApply(
            workspaceId, "race", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, originalBytes)]);
        undo.CommitPendingCapture(workspaceId, sequenceNumber: 1, affectedFiles: [dogFilePath], captureRace);
        await File.WriteAllTextAsync(dogFilePath, "// post-apply\n", CancellationToken.None);

        // A newer, non-overlapping operation is the pending LIFO snapshot while the race runs; a
        // lost claim must leave it (and its history entry) alone.
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var captureNewer = undo.CaptureBeforeApply(
            workspaceId, "newer", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(catFilePath, await File.ReadAllBytesAsync(catFilePath, CancellationToken.None))]);
        undo.CommitPendingCapture(workspaceId, sequenceNumber: 2, affectedFiles: [catFilePath], captureNewer);

        using var barrier = new Barrier(2);
        undo.BeforeSequenceClaim = () =>
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Second claimant never reached the claim barrier.");
        };

        var results = await Task.WhenAll(
            Task.Run(() => undo.RevertBySequenceAsync(workspaceId, 1, CancellationToken.None)),
            Task.Run(() => undo.RevertBySequenceAsync(workspaceId, 1, CancellationToken.None)));

        Assert.AreEqual(1, results.Count(r => r.Reverted),
            $"Exactly one claimant may restore; got reasons [{string.Join(", ", results.Select(r => r.Reason ?? "reverted"))}].");
        var loser = results.Single(r => !r.Reverted);
        Assert.AreEqual("unknown-sequence", loser.Reason);
        Assert.IsNull(loser.RevertedOperation);
        Assert.AreEqual(0, loser.AffectedFiles.Count);
        Assert.IsNull(loser.BlockingSequences);
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
        Assert.AreEqual("newer", undo.GetLastOperation(workspaceId)?.Description,
            "Neither claimant may drop another operation's pending snapshot.");
        undo.BeforeSequenceClaim = null;
        Assert.IsTrue((await undo.RevertBySequenceAsync(workspaceId, 2, CancellationToken.None)).Reverted,
            "The newer operation's history entry must survive the race.");
    }

    // undo-audit-commit-reuses-completed-capture: an audit record that owns no capture must never
    // promote whatever snapshot the per-workspace slot still holds (an earlier completed operation's,
    // or a failed/cancelled apply's uncommitted one).
    private void RecordCaptureFreeAudit(string workspaceId, string description) =>
        ChangeTracker.RecordChange(workspaceId, description, [], "audit_test", UndoCaptureId.None);

    [TestMethod]
    public async Task CompletedCapture_ThenCaptureFreeAudit_DoesNotPromoteEarlierSnapshot()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var applyA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply A\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyA.Success);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        RecordCaptureFreeAudit(workspaceId, "audit B");
        var sequenceB = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;

        var revertB = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsFalse(revertB.Reverted, "A capture-free audit owns no undo snapshot.");
        Assert.AreEqual("unknown-sequence", revertB.Reason);
        Assert.AreEqual("// apply A\n" + original, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None),
            "Undo-by-audit must not erase the earlier real apply.");

        Assert.AreEqual("Apply text edit to Dog.cs", UndoService.GetLastOperation(workspaceId)?.Description);
        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "Apply A must stay undoable by the latest-operation path.");
        Assert.AreEqual(original, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None));
        Assert.IsFalse((await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None)).Reverted,
            "A consumed by RevertAsync leaves no second by-sequence entry.");
    }

    [TestMethod]
    public async Task CompletedCapture_ThenCaptureFreeAudit_LeavesApplyUndoableBySequence()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var applyA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply A\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyA.Success);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        RecordCaptureFreeAudit(workspaceId, "audit B");

        var revertA = await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None);
        Assert.IsTrue(revertA.Reverted,
            $"The audit must not make apply A depend on a bogus later entry; reason={revertA.Reason}.");
        Assert.AreEqual(original, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task CaptureFreeAudit_AfterCompletedCapture_AddsNoHistory_ButStaysInLedger()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var applyA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply A\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyA.Success);

        RecordCaptureFreeAudit(workspaceId, "audit B");

        var changes = ChangeTracker.GetChanges(workspaceId);
        Assert.AreEqual(2, changes.Count, "A successful no-op audit still shows in the workspace changes ledger.");
        Assert.AreEqual("audit B", changes[1].Description);
        Assert.AreEqual("Apply text edit to Dog.cs", UndoService.GetLastOperation(workspaceId)?.Description,
            "The audit must not replace or re-describe the earlier snapshot.");
        var staleB = await UndoService.RevertBySequenceAsync(workspaceId, changes[1].SequenceNumber, CancellationToken.None);
        Assert.AreEqual("unknown-sequence", staleB.Reason, "The audit adds no revert-history entry.");
    }

    [TestMethod]
    public async Task FailedApply_LeavesPendingCaptureUndoable_AndAuditDoesNotPromoteIt()
    {
        // A capture whose apply failed is never recorded. It stays the LIFO RevertAsync target
        // (so a compensating rollback still works) but a later capture-free audit must not adopt it.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        UndoService.CaptureBeforeApply(
            workspaceId, "failed apply", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, originalBytes)]);
        await File.WriteAllTextAsync(dogFilePath, "// partial write of the failed apply\n", CancellationToken.None);

        RecordCaptureFreeAudit(workspaceId, "audit after failure");
        var sequenceAudit = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        var byAudit = await UndoService.RevertBySequenceAsync(workspaceId, sequenceAudit, CancellationToken.None);
        Assert.IsFalse(byAudit.Reverted, "The audit must not adopt the failed apply's pending capture.");
        Assert.AreEqual("unknown-sequence", byAudit.Reason);
        CollectionAssert.AreNotEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));

        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "The failed apply's pending capture must remain the retryable LIFO target.");
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancelledApply_LeavesPendingCaptureUndoable_AndAuditDoesNotPromoteIt()
    {
        // Cancelled mid-apply: the capture exists, the record never happens, and the first undo
        // attempt itself is cancelled. Neither leg may let a later audit adopt or lose the capture.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        UndoService.CaptureBeforeApply(
            workspaceId, "cancelled apply", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, originalBytes)]);
        await File.WriteAllTextAsync(dogFilePath, "// cancelled mid-write\n", CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => UndoService.RevertAsync(workspaceId, cancelled.Token));

        RecordCaptureFreeAudit(workspaceId, "audit after cancellation");
        var sequenceAudit = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;
        var byAudit = await UndoService.RevertBySequenceAsync(workspaceId, sequenceAudit, CancellationToken.None);
        Assert.IsFalse(byAudit.Reverted, "The audit must not adopt the cancelled apply's pending capture.");

        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None));
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task StaleCaptureId_Recorded_AfterNewerCapture_DoesNotPromoteNewer()
    {
        // Capture A is superseded by capture B before A's record lands (A's operation lost the
        // slot). Recording A must be refused and must not promote or hijack B's pending snapshot.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var captureA = UndoService.CaptureBeforeApply(
            workspaceId, "stale A", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None))]);
        UndoService.CaptureBeforeApply(
            workspaceId, "newer B", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(catFilePath, await File.ReadAllBytesAsync(catFilePath, CancellationToken.None))]);

        ChangeTracker.RecordChange(workspaceId, "stale A", [dogFilePath], "stale_test", captureA);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        Assert.AreEqual("unknown-sequence",
            (await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None)).Reason,
            "A stale capture id must not enter the revert history.");
        Assert.AreEqual("newer B", UndoService.GetLastOperation(workspaceId)?.Description,
            "The newer pending capture must survive the refused stale record.");
    }

    [TestMethod]
    public async Task CommitPromotion_NewerCaptureLandingBeforeSwap_SurvivesWithItsOwnUndo()
    {
        // CaptureBeforeApply takes no lock, so a newer capture B can replace the slot between
        // CommitPendingCapture's read of A's pending slot and its swap. The swap must not overwrite B.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var catBytes = await File.ReadAllBytesAsync(catFilePath, CancellationToken.None);
        using var undo = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
        var captureA = undo.CaptureBeforeApply(
            workspaceId, "op A", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None))]);
        UndoCaptureId captureB = default;
        undo.BeforeCommitSwap = () =>
        {
            undo.BeforeCommitSwap = null;
            captureB = undo.CaptureBeforeApply(
                workspaceId, "op B", preApplySolution: null,
                fileSnapshots: [FileSnapshotDto.FromExistingBytes(catFilePath, catBytes)]);
        };
        await File.WriteAllTextAsync(catFilePath, "// op B in flight\n", CancellationToken.None);

        undo.CommitPendingCapture(workspaceId, sequenceNumber: 1, affectedFiles: [dogFilePath], captureA);

        Assert.IsFalse(captureB.IsNone, "The seam must have run the newer capture.");
        Assert.AreEqual("op B", undo.GetLastOperation(workspaceId)?.Description,
            "A's promotion must not overwrite the newer capture in the slot.");
        Assert.IsTrue(await undo.RevertAsync(workspaceId, CancellationToken.None),
            "The newer capture must keep its own undo.");
        CollectionAssert.AreEqual(catBytes, await File.ReadAllBytesAsync(catFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task PersistFailure_LeavesPendingCapture_AuditDoesNotAdoptIt_RevertAsyncRestores()
    {
        // Real consumer: EditService captures, then the workspace write throws (read-only target).
        // No record is made, so the capture stays pending; a later
        // capture-free audit must not adopt it and RevertAsync must still restore.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        File.SetAttributes(dogFilePath, File.GetAttributes(dogFilePath) | FileAttributes.ReadOnly);
        try
        {
            // MSBuildWorkspace.TryApplyChanges writes the document itself and throws on the read-only
            // file, after EditService took its capture and before it records.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => EditService.ApplyTextEditsAsync(
                workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// never lands\n") },
                "apply_text_edit", CancellationToken.None));
            Assert.AreEqual(0, ChangeTracker.GetChanges(workspaceId).Count, "A failed apply records nothing.");
            Assert.AreEqual("Apply text edit to Dog.cs", UndoService.GetLastOperation(workspaceId)?.Description,
                "The failed apply's capture stays pending.");

            RecordCaptureFreeAudit(workspaceId, "audit after persist failure");
            var sequenceAudit = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;
            Assert.AreEqual("unknown-sequence",
                (await UndoService.RevertBySequenceAsync(workspaceId, sequenceAudit, CancellationToken.None)).Reason,
                "The audit must not adopt the failed apply's pending capture.");
        }
        finally
        {
            File.SetAttributes(dogFilePath, File.GetAttributes(dogFilePath) & ~FileAttributes.ReadOnly);
        }

        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "The failed apply's pending capture must remain the retryable LIFO target.");
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task SequentialCaptures_RecordedInCaptureOrder_EachPromotesUnderOwnSequence()
    {
        // Deterministic ordering of the interleaving "capture A, capture B, record A, record B":
        // A's record is refused (B owns the slot, B intact); B's record promotes under its own
        // sequence and stays undoable by sequence and by latest-operation.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        var catBytes = await File.ReadAllBytesAsync(catFilePath, CancellationToken.None);
        var captureA = UndoService.CaptureBeforeApply(
            workspaceId, "op A", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(dogFilePath, dogBytes)]);
        var captureB = UndoService.CaptureBeforeApply(
            workspaceId, "op B", preApplySolution: null,
            fileSnapshots: [FileSnapshotDto.FromExistingBytes(catFilePath, catBytes)]);
        Assert.AreNotEqual(captureA, captureB);
        Assert.IsFalse(captureA.IsNone);
        Assert.IsFalse(captureB.IsNone);
        await File.WriteAllTextAsync(catFilePath, "// op B\n", CancellationToken.None);

        ChangeTracker.RecordChange(workspaceId, "op A", [dogFilePath], "order_test", captureA);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;
        Assert.AreEqual("unknown-sequence",
            (await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None)).Reason);
        Assert.AreEqual("op B", UndoService.GetLastOperation(workspaceId)?.Description);

        ChangeTracker.RecordChange(workspaceId, "op B", [catFilePath], "order_test", captureB);
        var sequenceB = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;
        Assert.IsTrue(sequenceB > sequenceA);

        // Re-recording an already-promoted capture must not add a second history entry.
        ChangeTracker.RecordChange(workspaceId, "op B again", [catFilePath], "order_test", captureB);
        var sequenceRepeat = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;
        Assert.AreEqual("unknown-sequence",
            (await UndoService.RevertBySequenceAsync(workspaceId, sequenceRepeat, CancellationToken.None)).Reason);

        var revertB = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsTrue(revertB.Reverted, $"B must be undoable under its own sequence; reason={revertB.Reason}.");
        CollectionAssert.AreEqual(catBytes, await File.ReadAllBytesAsync(catFilePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task CaptureIds_AreDistinctAndNeverNone()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var ids = Enumerable.Range(0, 3)
            .Select(_ => UndoService.CaptureBeforeApply(workspace.WorkspaceId, "id probe", preApplySolution: null, fileSnapshots: []))
            .ToArray();
        Assert.IsTrue(UndoCaptureId.None.IsNone);
        Assert.IsTrue(ids.All(id => !id.IsNone));
        Assert.HasCount(3, ids.Distinct());
    }

    [TestMethod]
    public void CaptureBeforeApply_NonSolutionObject_ThrowsInvalidOperationException()
    {
        // The object-typed parameter is an internal wiring contract, not a caller-supplied
        // argument, so a violation must not surface as a caller-blaming ArgumentException.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => UndoService.CaptureBeforeApply("any-workspace", "wrong type", new object(), fileSnapshots: null));
    }

    [TestMethod]
    public async Task RevertAsync_ExplicitEmptyCapture_ConsumesOnlyEmptyOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var applyA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply A\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyA.Success);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;
        var postApplyBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        var postApplySolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var postApplyVersion = WorkspaceManager.GetCurrentVersion(workspaceId);
        await AssertDocumentTextAsync(postApplySolution, dogFilePath, "// apply A\n" + original);

        var captureEmptyB = UndoService.CaptureBeforeApply(workspaceId, "empty B", preApplySolution: null, fileSnapshots: []);
        ChangeTracker.RecordChange(workspaceId, "empty B", [], "empty_capture_test", captureEmptyB);
        var sequenceB = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;

        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "An explicit empty capture is a successful no-op undo.");
        CollectionAssert.AreEqual(postApplyBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
        Assert.AreSame(postApplySolution, WorkspaceManager.GetCurrentSolution(workspaceId),
            "An empty undo must not reload or replace the current solution.");
        Assert.AreEqual(postApplyVersion, WorkspaceManager.GetCurrentVersion(workspaceId));
        Assert.IsNull(UndoService.GetLastOperation(workspaceId));
        Assert.IsFalse(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "The empty operation must be consumed exactly once.");
        var repeatB = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsFalse(repeatB.Reverted);
        Assert.AreEqual("unknown-sequence", repeatB.Reason);

        var revertA = await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None);
        Assert.IsTrue(revertA.Reverted, "Undoing B must preserve A's sequence history.");
        Assert.AreEqual(original, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None));
        await AssertDocumentTextAsync(WorkspaceManager.GetCurrentSolution(workspaceId), dogFilePath, original);
    }

    [TestMethod]
    public async Task RevertBySequence_ExplicitEmptyCapture_PreservesLaterPendingApplyAndEarlierHistory()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogOriginal = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var catOriginal = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        var applyA = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply A\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyA.Success);
        var sequenceA = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        var captureEmptyB = UndoService.CaptureBeforeApply(workspaceId, "empty B", preApplySolution: null, fileSnapshots: []);
        ChangeTracker.RecordChange(workspaceId, "empty B", [], "empty_capture_test", captureEmptyB);
        var sequenceB = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;
        var applyC = await EditService.ApplyTextEditsAsync(
            workspaceId, catFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply C\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applyC.Success);
        var sequenceC = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;
        var pendingC = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(pendingC);
        var dogBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        var catBytes = await File.ReadAllBytesAsync(catFilePath, CancellationToken.None);
        var currentSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var currentVersion = WorkspaceManager.GetCurrentVersion(workspaceId);
        await AssertDocumentTextAsync(currentSolution, dogFilePath, "// apply A\n" + dogOriginal);
        await AssertDocumentTextAsync(currentSolution, catFilePath, "// apply C\n" + catOriginal);

        var revertB = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsTrue(revertB.Reverted);
        Assert.AreEqual("empty B", revertB.RevertedOperation);
        Assert.IsNull(revertB.Reason);
        Assert.AreEqual(0, revertB.AffectedFiles.Count);
        Assert.IsNull(revertB.BlockingSequences);
        CollectionAssert.AreEqual(dogBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
        CollectionAssert.AreEqual(catBytes, await File.ReadAllBytesAsync(catFilePath, CancellationToken.None));
        Assert.AreSame(currentSolution, WorkspaceManager.GetCurrentSolution(workspaceId));
        Assert.AreEqual(currentVersion, WorkspaceManager.GetCurrentVersion(workspaceId));
        Assert.AreEqual(pendingC, UndoService.GetLastOperation(workspaceId),
            "Undoing earlier empty B must retain C as the pending LIFO operation.");
        var repeatB = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsFalse(repeatB.Reverted);
        Assert.AreEqual("unknown-sequence", repeatB.Reason);

        Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "C must remain undoable through the LIFO route.");
        Assert.AreEqual(catOriginal, await File.ReadAllTextAsync(catFilePath, CancellationToken.None));
        CollectionAssert.AreEqual(dogBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
        await AssertDocumentTextAsync(WorkspaceManager.GetCurrentSolution(workspaceId), catFilePath, catOriginal);
        await AssertDocumentTextAsync(WorkspaceManager.GetCurrentSolution(workspaceId), dogFilePath, "// apply A\n" + dogOriginal);
        var repeatC = await UndoService.RevertBySequenceAsync(workspaceId, sequenceC, CancellationToken.None);
        Assert.IsFalse(repeatC.Reverted);
        Assert.AreEqual("unknown-sequence", repeatC.Reason);
        var revertA = await UndoService.RevertBySequenceAsync(workspaceId, sequenceA, CancellationToken.None);
        Assert.IsTrue(revertA.Reverted, "A's earlier history must survive both B and C being undone.");
        Assert.AreEqual(dogOriginal, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None));
        await AssertDocumentTextAsync(WorkspaceManager.GetCurrentSolution(workspaceId), dogFilePath, dogOriginal);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Revert_ExplicitEmptyCapture_CancellationRetainsCaptureAndHistory(bool bySequence)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspaceId);
        var captureEmptyB = UndoService.CaptureBeforeApply(workspaceId, "empty B", preApplySolution: null, fileSnapshots: []);
        ChangeTracker.RecordChange(workspaceId, "empty B", [], "empty_capture_test", captureEmptyB);
        var sequenceB = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;
        var pendingB = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(pendingB);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            if (bySequence)
                await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, cts.Token);
            else
                await UndoService.RevertAsync(workspaceId, cts.Token);
        });
        Assert.AreEqual(pendingB, UndoService.GetLastOperation(workspaceId));
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspaceId));

        // Retry through the opposite route to prove both capture stores survived cancellation.
        if (bySequence)
            Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None));
        else
            Assert.IsTrue((await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None)).Reverted);
        Assert.IsNull(UndoService.GetLastOperation(workspaceId));
        Assert.IsFalse(await UndoService.RevertAsync(workspaceId, CancellationToken.None));
        var repeat = await UndoService.RevertBySequenceAsync(workspaceId, sequenceB, CancellationToken.None);
        Assert.IsFalse(repeat.Reverted);
        Assert.AreEqual("unknown-sequence", repeat.Reason);
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspaceId));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Revert_NullSolutionAndMissingOrContradictoryFileSnapshots_RemainsInvalid(bool bySequence, bool contradictoryEmptyCapture)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspaceId);
        var invalidCapture = UndoService.CaptureBeforeApply(workspaceId, "invalid capture", preApplySolution: null,
            fileSnapshots: contradictoryEmptyCapture ? [] : null);
        ChangeTracker.RecordChange(workspaceId, "invalid capture",
            contradictoryEmptyCapture ? [workspace.GetPath("SampleLib", "Dog.cs")] : [], "invalid_capture_test", invalidCapture);
        var sequence = ChangeTracker.GetChanges(workspaceId).Single().SequenceNumber;

        if (bySequence)
        {
            var result = await UndoService.RevertBySequenceAsync(workspaceId, sequence, CancellationToken.None);
            Assert.IsFalse(result.Reverted);
            Assert.AreEqual("revert-failed", result.Reason);
        }
        else
        {
            Assert.IsFalse(await UndoService.RevertAsync(workspaceId, CancellationToken.None));
        }

        Assert.IsNotNull(UndoService.GetLastOperation(workspaceId), "An invalid capture must not be consumed as a success.");
        var retry = await UndoService.RevertBySequenceAsync(workspaceId, sequence, CancellationToken.None);
        Assert.IsFalse(retry.Reverted);
        Assert.AreEqual("revert-failed", retry.Reason, "Invalid capture history must remain retryable.");
        Assert.AreSame(originalSolution, WorkspaceManager.GetCurrentSolution(workspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspaceId));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Revert_SolutionAndEmptyFileSnapshots_UsesSolutionFallback(bool bySequence)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var originalSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        await MaterializeDocumentTextAsync(originalSolution, dogFilePath);
        var apply = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// apply\n") },
            "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(apply.Success);
        Assert.AreNotEqual(originalText, await File.ReadAllTextAsync(dogFilePath, CancellationToken.None));
        var captureVersion = WorkspaceManager.GetCurrentVersion(workspaceId);
        UndoService.Clear(workspaceId);
        var fallbackCapture = UndoService.CaptureBeforeApply(workspaceId, "solution fallback", originalSolution, fileSnapshots: []);
        ChangeTracker.RecordChange(workspaceId, "solution fallback", new[] { dogFilePath }, "solution_capture_test", fallbackCapture);
        var sequence = ChangeTracker.GetChanges(workspaceId).Last().SequenceNumber;

        if (bySequence)
            Assert.IsTrue((await UndoService.RevertBySequenceAsync(workspaceId, sequence, CancellationToken.None)).Reverted);
        else
            Assert.IsTrue(await UndoService.RevertAsync(workspaceId, CancellationToken.None));

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None));
        await AssertDocumentTextAsync(WorkspaceManager.GetCurrentSolution(workspaceId), dogFilePath, originalText);
        Assert.IsNull(UndoService.GetLastOperation(workspaceId));
        var repeat = await UndoService.RevertBySequenceAsync(workspaceId, sequence, CancellationToken.None);
        Assert.IsFalse(repeat.Reverted);
        Assert.AreEqual("unknown-sequence", repeat.Reason);
        Assert.AreEqual(captureVersion, WorkspaceManager.GetCurrentVersion(workspaceId));
    }

    private static async Task AssertDocumentTextAsync(Solution solution, string filePath, string expected)
    {
        var documentId = solution.GetDocumentIdsWithFilePath(filePath).Single();
        var document = solution.GetDocument(documentId);
        Assert.IsNotNull(document);
        Assert.AreEqual(expected, (await document.GetTextAsync(CancellationToken.None)).ToString());
    }

    // ---- composite-apply-undo-encoding-still-lossy: solution-snapshot revert encoding fidelity ----
    //
    // `RevertFromSolutionSnapshotAsync` collapsed each restore to a bare string, discarding the
    // pre-apply `SourceText.Encoding`, then wrote through the encoding-less `AtomicFileWriter`
    // overload — so a UTF-8-BOM or UTF-16 file reverted through the solution-snapshot path was
    // silently rewritten as UTF-8-no-BOM. `EditService` always supplies file snapshots, so the
    // solution-snapshot branch is unreachable from a normal apply; both tests below force it by
    // calling `CaptureBeforeApply` directly with `fileSnapshots: null`. They cover the two
    // collection phases separately: the Solution-diff phase and the disk-walk safety net.

    /// <summary>
    /// Phase-1 (Solution diff) coverage. The <c>utf8-nobom</c> row is the inverse guard: Roslyn
    /// reports a no-preamble UTF-8 <c>SourceText.Encoding</c> for a BOM-less file, which
    /// <c>AtomicFileWriter.ResolveWriteEncoding(Encoding?)</c> must normalize so the revert does
    /// not ADD a BOM to a file that never had one.
    /// </summary>
    [TestMethod]
    [DataRow("utf8-nobom")]
    [DataRow("utf8-bom")]
    [DataRow("utf16-bom")]
    public async Task RevertAsync_SolutionSnapshotPath_SolutionDiff_PreservesOriginalEncoding(string encodingKind)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var (encoding, preamble, originalBytes) = await SeedEncodedFileAsync(dogFilePath, encodingKind);
        await workspace.ReloadAsync(CancellationToken.None);

        var preApplySolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        await MaterializeDocumentTextAsync(preApplySolution, dogFilePath);

        // A real apply, so the snapshot is diffed against a genuinely mutated workspace + disk.
        var applyResult = await EditService.ApplyTextEditsAsync(
            workspaceId,
            dogFilePath,
            new[] { new TextEditDto(1, 1, 1, 1, "// apply\n") },
            "apply_text_edit",
            CancellationToken.None);
        Assert.IsTrue(applyResult.Success, "Precondition: the apply should succeed.");

        // EditService records file snapshots, which route RevertAsync down the fast path. Overwrite
        // the pending snapshot with a solution-only one to force the solution-snapshot branch.
        UndoService.CaptureBeforeApply(
            workspaceId,
            "solution-snapshot encoding round-trip",
            preApplySolution,
            fileSnapshots: null);

        Assert.IsTrue(
            await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "The solution-snapshot revert should succeed.");

        CollectionAssert.AreEqual(
            originalBytes,
            await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None),
            $"revert_last_apply's solution-snapshot path must restore the exact pre-apply {encodingKind} bytes, " +
            "including the byte-order mark, instead of rewriting the file as UTF-8-no-BOM.");
    }

    /// <summary>
    /// undo-restore-source-kind: the solution-diff restore must carry the pre-apply
    /// <see cref="SourceCodeKind"/> back, not only the text. The observable effect is the solution
    /// handed to <c>TryApplyChanges</c> (the workspace is reloaded from MSBuild afterwards, which
    /// re-derives kinds from the project files). Kinds are altered synthetically through the Solution API.
    /// </summary>
    [TestMethod]
    public async Task RevertAsync_SolutionSnapshot_RestoresPreApplySourceCodeKind()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var preApply = WorkspaceManager.GetCurrentSolution(workspaceId);
        var preApplyDoc = preApply.GetDocumentIdsWithFilePath(dogFilePath).Select(preApply.GetDocument).Single()!;
        Assert.AreEqual(SourceCodeKind.Regular, preApplyDoc.SourceCodeKind, "Precondition: pre-apply kind.");
        await preApplyDoc.GetTextAsync(CancellationToken.None);

        var applied = preApply
            .WithDocumentSourceCodeKind(preApplyDoc.Id, SourceCodeKind.Script)
            .WithDocumentText(preApplyDoc.Id, Microsoft.CodeAnalysis.Text.SourceText.From("// applied\n"));
        var manager = new CapturingWorkspaceManager(WorkspaceManager, applied);
        using var undo = new UndoService(NullLogger<UndoService>.Instance, manager);
        undo.CaptureBeforeApply(workspaceId, "kind change", preApply, fileSnapshots: null);

        Assert.IsTrue(await undo.RevertAsync(workspaceId, CancellationToken.None));

        Assert.IsNotNull(manager.AppliedSolution, "The revert must hand a target solution to TryApplyChanges.");
        var restored = manager.AppliedSolution.GetDocument(preApplyDoc.Id)!;
        Assert.AreEqual(SourceCodeKind.Regular, restored.SourceCodeKind,
            "Undo must restore the pre-apply SourceCodeKind, not leave the applied Script kind.");
        Assert.AreEqual(
            (await preApplyDoc.GetTextAsync(CancellationToken.None)).ToString(),
            (await restored.GetTextAsync(CancellationToken.None)).ToString());
    }

    private sealed class CapturingWorkspaceManager(IWorkspaceManager inner, Solution current)
        : Helpers.DelegatingWorkspaceManager(inner)
    {
        public Solution? AppliedSolution { get; private set; }

        public override Solution GetCurrentSolution(string workspaceId) => current;

        public override bool TryApplyChanges(string workspaceId, Solution newSolution)
        {
            AppliedSolution = newSolution;
            return true;
        }
    }

    /// <summary>
    /// Phase-2 (disk-walk safety net) coverage: the drift is disk-only, so
    /// <c>preApplySolution.GetChanges(currentSolution)</c> is empty and the restore is collected by
    /// the disk walk instead. UTF-16 is the strongest signal — a dropped preamble there also
    /// corrupts every character, not just the first bytes.
    /// </summary>
    [TestMethod]
    public async Task RevertAsync_SolutionSnapshotPath_DiskWalk_PreservesOriginalEncoding()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        ChangeTracker.Clear(workspaceId);
        UndoService.Clear(workspaceId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var (encoding, preamble, originalBytes) = await SeedEncodedFileAsync(dogFilePath, "utf16-bom");
        await workspace.ReloadAsync(CancellationToken.None);

        var preApplySolution = WorkspaceManager.GetCurrentSolution(workspaceId);
        await MaterializeDocumentTextAsync(preApplySolution, dogFilePath);
        UndoService.CaptureBeforeApply(
            workspaceId,
            "disk-walk encoding round-trip",
            preApplySolution,
            fileSnapshots: null);

        // Disk-only drift: the workspace solution is untouched, so phase 1 finds nothing.
        var originalText = encoding.GetString(originalBytes, preamble.Length, originalBytes.Length - preamble.Length);
        await File.WriteAllBytesAsync(
            dogFilePath,
            preamble.Concat(encoding.GetBytes("// drifted\n" + originalText)).ToArray(),
            CancellationToken.None);

        Assert.IsTrue(
            await UndoService.RevertAsync(workspaceId, CancellationToken.None),
            "The disk-walk safety net should restore the drifted file and report success.");

        CollectionAssert.AreEqual(
            originalBytes,
            await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None),
            "The disk-walk restore must re-emit the original UTF-16 byte-order mark and encoding.");
    }

    /// <summary>
    /// Rewrites <paramref name="filePath"/> in the requested encoding, preserving its text, and
    /// returns the encoding, its preamble, and the exact bytes now on disk.
    /// </summary>
    private static async Task<(Encoding Encoding, byte[] Preamble, byte[] OriginalBytes)> SeedEncodedFileAsync(
        string filePath,
        string encodingKind)
    {
        var text = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        Encoding encoding = encodingKind switch
        {
            "utf16-bom" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var preamble = encoding.GetPreamble();
        var bytes = preamble.Concat(encoding.GetBytes(text)).ToArray();
        await File.WriteAllBytesAsync(filePath, bytes, CancellationToken.None);
        return (encoding, preamble, bytes);
    }

    /// <summary>
    /// Forces the snapshot solution to hold <paramref name="filePath"/>'s text (and therefore its
    /// detected <c>SourceText.Encoding</c>) in memory. Roslyn loads document text lazily, so an
    /// un-materialized snapshot would re-read whatever is on disk at revert time and conclude
    /// nothing changed. Real solution-based callers (<c>RefactoringService</c>) always read the
    /// document while computing their change, so this only reproduces their state.
    /// </summary>
    private static async Task MaterializeDocumentTextAsync(Solution solution, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null
                && string.Equals(Path.GetFullPath(d.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(document, $"Expected '{filePath}' to be part of the loaded solution.");

        var text = await document!.GetTextAsync(CancellationToken.None);
        Assert.IsNotNull(
            text.Encoding,
            "Precondition: Roslyn must have detected an encoding for the seeded file — otherwise the " +
            "test cannot distinguish a preserved encoding from the UTF-8-no-BOM fallback.");
    }
}
