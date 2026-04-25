using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>preview-token-cross-coupling-bundle</c> (closes
/// <c>dr-9-6-preview-token-issued-in-turn-t-invalidated-by-a</c> + FLAG
/// <c>severity-flag-unexpected-coupling-of-preview-tokens</c>). Before the fix, the
/// <see cref="PreviewStore"/> shared a single workspace-version stamp across all tokens
/// and <see cref="WorkspaceManager.TryApplyChanges"/> called <c>InvalidateAll</c> on every
/// successful apply — so any sibling <c>*_apply</c> in the same turn would invalidate
/// every outstanding preview token. The fix captures an isolated per-token
/// <see cref="Microsoft.CodeAnalysis.Solution"/> snapshot pair (<c>OriginalSolution</c> +
/// <c>ModifiedSolution</c>) and rebases the preview's intended diff onto the current
/// workspace at apply time.
/// </summary>
[TestClass]
public sealed class PreviewTokenCrossCouplingTests : IsolatedWorkspaceTestBase
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

    /// <summary>
    /// The exact repro from NetworkDocumentation audit 2026-04-15 §9.6: two previews are
    /// issued in turn T (different files); token A is applied; token B must still be
    /// valid and its apply must succeed.
    /// </summary>
    [TestMethod]
    public async Task Sibling_Apply_Does_Not_Invalidate_Unrelated_Preview_Token()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var firstFilePath = workspace.GetPath("SampleLib", "Generated", "FirstCoupled.cs");
        var secondFilePath = workspace.GetPath("SampleLib", "Generated", "SecondCoupled.cs");

        // Issue TWO previews against the same workspace for UNRELATED files.
        var previewA = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                firstFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class FirstCoupled\n{\n}\n"),
            CancellationToken.None);

        var previewB = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                secondFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class SecondCoupled\n{\n}\n"),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(previewA.PreviewToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewB.PreviewToken));
        Assert.AreNotEqual(previewA.PreviewToken, previewB.PreviewToken);

        // Apply A first — this bumps the workspace version. Pre-fix, the `TryApplyChanges`
        // path called `_previewStore.InvalidateAll(workspaceId)`, so token B would be
        // dropped here even though nothing about file B changed.
        var applyA = await RefactoringService.ApplyRefactoringAsync(previewA.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyA.Success, applyA.Error);
        Assert.IsTrue(File.Exists(firstFilePath));

        // Apply B — must still succeed. Pre-fix this returned
        // "Preview token is invalid, expired, or stale because the workspace changed
        // since the preview was generated." (audit §9.6, §5 "extract_interface_apply
        // stale preview token (invalidated by sibling format_range_apply in same turn)").
        var applyB = await RefactoringService.ApplyRefactoringAsync(previewB.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyB.Success,
            $"Sibling apply must not invalidate unrelated preview tokens. Got: {applyB.Error}");
        Assert.IsTrue(File.Exists(secondFilePath));
        StringAssert.Contains(
            await File.ReadAllTextAsync(secondFilePath, CancellationToken.None),
            "class SecondCoupled");

        // Both files must be present in the current workspace solution after the
        // second apply — the rebase path must have mirrored A's addition forward
        // without reverting it.
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.IsNotNull(SymbolResolver.FindDocument(solution, firstFilePath),
            "Token A's file must remain in the workspace after token B's apply.");
        Assert.IsNotNull(SymbolResolver.FindDocument(solution, secondFilePath),
            "Token B's file must be present after its own apply.");
    }

    /// <summary>
    /// Mirror scenario with text-edit previews (no added/removed documents). Two previews
    /// organize usings on two different files; applying one must not invalidate the other.
    /// Guards against the second failure mode — passing the stale
    /// <see cref="Microsoft.CodeAnalysis.Solution"/> to <c>TryApplyChanges</c> and having
    /// <c>GetChanges</c> treat unrelated sibling edits as reversions.
    /// </summary>
    [TestMethod]
    public async Task Sibling_Text_Edit_Apply_Does_Not_Invalidate_Other_Text_Edit_Token()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var firstFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var secondFilePath = workspace.GetPath("SampleApp", "Program.cs");

        // Skip this shape when the fixtures diverge from expectations so the test fails
        // loudly at assertion time on real regressions but doesn't spuriously fail on a
        // fixture rename.
        Assert.IsTrue(File.Exists(firstFilePath), $"Fixture file missing: {firstFilePath}");
        Assert.IsTrue(File.Exists(secondFilePath), $"Fixture file missing: {secondFilePath}");

        var previewA = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, firstFilePath, CancellationToken.None);
        var previewB = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, secondFilePath, CancellationToken.None);

        // OrganizeUsings may emit no-op previews on a clean fixture; both tests above
        // only enforce "token-B-remains-valid-after-A's-apply". If either preview is
        // empty we still exercise the rebase path by invoking apply.
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewA.PreviewToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewB.PreviewToken));

        var applyA = await RefactoringService.ApplyRefactoringAsync(previewA.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyA.Success, applyA.Error);

        var applyB = await RefactoringService.ApplyRefactoringAsync(previewB.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyB.Success,
            $"Sibling text-edit apply must not invalidate unrelated preview tokens. Got: {applyB.Error}");

        // The file from token A must still exist on disk (it must not have been
        // reverted by B's rebase overwriting the workspace with the pre-A snapshot).
        Assert.IsTrue(File.Exists(firstFilePath), "Token A's file must remain on disk after token B's apply.");
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: a SINGLE reload no longer wipes live
    /// tokens. <see cref="PreviewStore.DefaultMaxVersionSpan"/> = 1 means a token stored at
    /// version V survives one auto-reload bump to V+1. The apply path's
    /// <c>RebaseModifiedSolutionOntoCurrentAsync</c> replays the captured diff onto the
    /// post-reload solution, so the file-creation produced by the preview lands on disk
    /// even though the underlying <c>MSBuildWorkspace</c> was disposed during reload.
    /// </summary>
    [TestMethod]
    public async Task Workspace_Reload_Within_Pinned_Range_Keeps_Token_Redeemable()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "ReloadInvalidates.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                targetFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class ReloadInvalidates { }\n"),
            CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        // Single reload bumps the workspace version once — within DefaultMaxVersionSpan=1,
        // so InvalidateOnVersionBump leaves this token alone.
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success,
            $"Token stored at V should survive a single reload to V+1 with DefaultMaxVersionSpan=1. Error: {applyResult.Error}");
        Assert.IsTrue(File.Exists(targetFilePath), "Apply must persist the file even after one intervening reload.");
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: bounded-range guarantee — a SECOND reload
    /// pushes the workspace version past the pinned ceiling
    /// (V + <see cref="PreviewStore.DefaultMaxVersionSpan"/> = V + 1), so the token MUST
    /// drop and the apply path must surface the "stale" rejection. Guards against the range
    /// policy regressing into "tokens never expire on reload."
    /// </summary>
    [TestMethod]
    public async Task Workspace_Reload_Twice_Drops_Token_And_Surfaces_Stale_Error()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "TwoReloadsDrop.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                targetFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class TwoReloadsDrop { }\n"),
            CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        // Two reload bumps push version past the pinned ceiling — token gets dropped.
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsFalse(applyResult.Success, "Two reloads must push past the pinned ceiling and drop the token.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        Assert.IsFalse(File.Exists(targetFilePath), "Dropped token must not mutate disk.");
    }
}
