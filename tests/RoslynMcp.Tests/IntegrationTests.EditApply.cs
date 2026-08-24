using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public class IntegrationTests_EditApply : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task Rename_Preview_And_Apply_Update_Isolated_Workspace_Copy()
    {
        using var workspace = await CreateIsolatedWorkspaceAsync();
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var serviceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewRenameAsync(
            workspace.WorkspaceId,
            SymbolLocator.BySource(dogFilePath, 3, 14),
            "Hound",
            CancellationToken.None);
        Assert.IsTrue(preview.Changes.Count > 0, "Rename preview should produce file changes.");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "test_apply",
            CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var dogContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
        StringAssert.Contains(dogContents, "class Hound");
        StringAssert.Contains(serviceContents, "new Hound()");
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: tokens survive a single auto-reload
    /// (one version bump within DefaultMaxVersionSpan = 1) but a second reload pushes past
    /// the pinned ceiling and drops the token, surfacing the "stale" rejection. End-to-end
    /// integration variant — mirrors PreviewStoreTests' unit-level coverage and ensures the
    /// rejection still flows up through <c>RefactoringService.ApplyRefactoringAsync</c>.
    /// </summary>
    [TestMethod]
    public async Task Preview_Token_Is_Rejected_After_Two_Reloads()
    {
        using var workspace = await CreateIsolatedWorkspaceAsync();
        var serviceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            CancellationToken.None);
        // Two reloads push the version past the pinned ceiling (V + 1) — token dropped.
        await workspace.ReloadAsync();
        await workspace.ReloadAsync();

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "test_apply",
            CancellationToken.None);
        Assert.IsFalse(applyResult.Success, "Two reloads must push past the pinned ceiling and reject the apply.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
    }

    [TestMethod]
    public async Task Organize_Usings_Apply_Removes_Unused_Using_In_Isolated_Copy()
    {
        using var workspace = await CreateIsolatedWorkspaceAsync();
        var serviceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "test_apply",
            CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
        Assert.IsFalse(serviceContents.Contains("using System.Threading;"));
    }

    [TestMethod]
    public async Task Format_Document_Apply_Normalizes_Isolated_Copy()
    {
        using var workspace = await CreateIsolatedWorkspaceAsync();
        var serviceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var preview = await RefactoringService.PreviewFormatDocumentAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "test_apply",
            CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
        Assert.IsFalse(serviceContents.Contains("MakeThemSpeak(    IEnumerable<IAnimal>     animals   )"));
    }
}
