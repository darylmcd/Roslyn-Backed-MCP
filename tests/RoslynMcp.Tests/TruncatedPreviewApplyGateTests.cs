using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #4 — regression guard for the apply-truncation safety gate. Before the fix, a
/// preview whose diff exceeded the 64 KB per-solution cap was truncated in the caller-
/// facing response while the stored Solution contained the full change set — the apply
/// proceeded blind. The fix (a) tags truncated previews in PreviewStore, (b) refuses
/// to apply a tagged preview unless the caller passes force=true.
/// </summary>
[TestClass]
public sealed class TruncatedPreviewApplyGateTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Apply_Refuses_Truncated_Preview_Without_Force()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        // Simulate what a truncated preview looks like: stash the workspace's current
        // solution under a token marked `diffTruncated=true`. No real disk mutation occurs
        // if the apply is refused, which is exactly what this test asserts.
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspaceId);
        var token = PreviewStore.Store(
            workspaceId,
            solution,
            version,
            description: "Item #4 truncation gate probe",
            diffTruncated: true);

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", force: false, CancellationToken.None);

        Assert.IsFalse(result.Success, "Truncated preview must refuse to apply without force.");
        Assert.IsNotNull(result.Error, "Refusal must carry an actionable error message.");
        Assert.IsTrue(
            result.Error!.Contains("truncated", StringComparison.OrdinalIgnoreCase) &&
            result.Error.Contains("force", StringComparison.OrdinalIgnoreCase),
            $"Refusal message must mention both 'truncated' and 'force'. Got: {result.Error}");
    }

    [TestMethod]
    public async Task Apply_Allows_Truncated_Preview_With_Force()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspaceId);
        var token = PreviewStore.Store(
            workspaceId,
            solution,
            version,
            description: "Item #4 truncation gate probe (force)",
            diffTruncated: true);

        // force=true opts in to the blind apply. Since we're applying the same Solution
        // that's already loaded, no real disk mutation occurs, but the apply call must
        // proceed past the truncation gate (Success=true, or if Success=false it's for
        // reasons other than the gate).
        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", force: true, CancellationToken.None);

        // The apply may have nothing to do (identity solution) — both Success paths
        // are acceptable. What MUST be true: the Error does NOT mention truncation.
        if (!result.Success && result.Error is not null)
        {
            Assert.IsFalse(
                result.Error.Contains("truncated", StringComparison.OrdinalIgnoreCase),
                $"force=true must bypass the truncation gate. Error was: {result.Error}");
        }
    }

    [TestMethod]
    public async Task Apply_Allows_Non_Truncated_Preview_By_Default()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspaceId);
        var token = PreviewStore.Store(
            workspaceId,
            solution,
            version,
            description: "Item #4 non-truncated probe",
            diffTruncated: false);

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", force: false, CancellationToken.None);

        // Identity solution apply; again the gate must not fire. Any failure MUST not
        // reference truncation.
        if (!result.Success && result.Error is not null)
        {
            Assert.IsFalse(
                result.Error.Contains("truncated", StringComparison.OrdinalIgnoreCase),
                $"Non-truncated preview must not hit the truncation gate. Error was: {result.Error}");
        }
    }

    [TestMethod]
    [DataRow(PreviewKind.CodeFix)]
    [DataRow(PreviewKind.CodeAction)]
    public async Task ChangesShapedPreview_DerivesTruncationAndRequiresForce(PreviewKind kind)
    {
        // The SolutionDiffHelper emits a synthetic FileChangeDto with
        // FilePath == TruncatedSentinelFilePath when its cap fires. The convenience Store
        // overload inspects the list and wires the flag automatically so callers don't
        // have to remember.
        var truncatedChanges = new List<FileChangeDto>
        {
            new("Some/Real/File.cs", "@@ -1 +1 @@\n-old\n+new"),
            new(SolutionDiffHelper.TruncatedSentinelFilePath, "# FLAG-6A omitted 12 files"),
        };

        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var token = PreviewStore.Store(
            workspaceId,
            solution,
            WorkspaceManager.GetCurrentVersion(workspaceId),
            $"{kind} sentinel truncation probe",
            truncatedChanges,
            kind);

        var stored = PreviewStore.Retrieve(token);
        Assert.IsNotNull(stored);
        Assert.IsTrue(stored.Value.DiffTruncated,
            $"The changes-shaped {kind} producer contract must derive DiffTruncated from the sentinel.");
        Assert.AreEqual(kind, PreviewStore.PeekKind(token));

        var refused = await RefactoringService.ApplyRefactoringAsync(
            token, "test_apply", force: false, CancellationToken.None);
        Assert.IsFalse(refused.Success, $"A truncated {kind} preview must be refused without force.");
        StringAssert.Contains(refused.Error!, "force");

        var forced = await RefactoringService.ApplyRefactoringAsync(
            token, "test_apply", force: true, CancellationToken.None);
        if (!forced.Success && forced.Error is not null)
        {
            Assert.IsFalse(
                forced.Error.Contains("truncated", StringComparison.OrdinalIgnoreCase),
                $"force=true must bypass the {kind} truncation gate. Error was: {forced.Error}");
        }
    }
}
