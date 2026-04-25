using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FileOperationIntegrationTests : IsolatedWorkspaceTestBase
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
    public async Task Create_File_Preview_And_Apply_Adds_File_To_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var newFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                newFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class Bird\n{\n}\n"),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(newFilePath));
        StringAssert.Contains(await File.ReadAllTextAsync(newFilePath, CancellationToken.None), "class Bird");

        var document = SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), newFilePath);
        Assert.IsNotNull(document, "Created document should be present after reload.");
    }

    [TestMethod]
    public async Task Delete_File_Preview_And_Apply_Removes_File_From_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var preview = await FileOperationService.PreviewDeleteFileAsync(
            workspace.WorkspaceId,
            new DeleteFileDto(targetFilePath),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(targetFilePath));
        Assert.IsNull(SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), targetFilePath));
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: previews survive a single auto-reload
    /// (one workspace-version bump) inside the pinned range, but two reloads push past
    /// <see cref="PreviewStore.DefaultMaxVersionSpan"/> = 1 and the token drops with the
    /// "stale" error. Mirrors the create-file integration shape that this test originally
    /// covered, updated to the post-bundle pinned-range contract.
    /// </summary>
    [TestMethod]
    public async Task File_Operation_Preview_Token_Is_Rejected_After_Two_Reloads()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var newFilePath = workspace.GetPath("SampleLib", "Generated", "StaleBird.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto("SampleLib", newFilePath, "namespace SampleLib.Generated;\n\npublic sealed class StaleBird { }\n"),
            CancellationToken.None);

        // Two reload bumps push past the pinned ceiling (V + 1) — token gets dropped on
        // the second reload's InvalidateOnVersionBump.
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsFalse(applyResult.Success, "Stale previews (two reloads past pinned range) should be rejected.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        Assert.IsFalse(File.Exists(newFilePath));
    }

    [TestMethod]
    public async Task Move_File_Preview_And_Apply_Updates_Isolated_Workspace_Copy()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var destinationFilePath = workspace.GetPath("SampleLib", "Animals", "Dog.cs");

        var preview = await FileOperationService.PreviewMoveFileAsync(
            workspace.WorkspaceId,
            new MoveFileDto(sourceFilePath, destinationFilePath, null, UpdateNamespace: true),
            CancellationToken.None);

        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(sourceFilePath));
        Assert.IsTrue(File.Exists(destinationFilePath));
        var movedContents = await File.ReadAllTextAsync(destinationFilePath, CancellationToken.None);
        StringAssert.Contains(movedContents, "namespace SampleLib.Animals");
    }
}
