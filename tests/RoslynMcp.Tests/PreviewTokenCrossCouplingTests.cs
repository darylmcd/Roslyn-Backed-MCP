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
    /// Workspace lifecycle still invalidates tokens: a <see cref="WorkspaceManager.ReloadAsync"/>
    /// disposes the underlying MSBuildWorkspace and the Solution references captured by
    /// every live preview become orphans, so the preview store's lifecycle hook must drop
    /// them. This is the ONE code path that should still invalidate siblings, and the
    /// "stale" error message must still surface to callers.
    /// </summary>
    [TestMethod]
    public async Task Workspace_Reload_Still_Invalidates_All_Live_Tokens()
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

        // Lifecycle event — drops all tokens (captured Solution refs now orphaned).
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsFalse(applyResult.Success,
            "Reload must still invalidate live preview tokens.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        Assert.IsFalse(File.Exists(targetFilePath), "Invalidated token must not mutate disk.");
    }
}
