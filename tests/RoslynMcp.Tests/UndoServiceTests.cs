using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

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
}
