using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FileOperationIntegrationTests : TestBase
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
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var newFilePath = Path.Combine(copiedRoot, "SampleLib", "Generated", "Bird.cs");

            var preview = await FileOperationService.PreviewCreateFileAsync(
                workspaceId,
                new CreateFileDto(
                    "SampleLib",
                    newFilePath,
                    "namespace SampleLib.Generated;\n\npublic sealed class Bird\n{\n}\n"),
                CancellationToken.None);

            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)));

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsTrue(File.Exists(newFilePath));
            StringAssert.Contains(await File.ReadAllTextAsync(newFilePath, CancellationToken.None), "class Bird");

            var document = SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspaceId), newFilePath);
            Assert.IsNotNull(document, "Created document should be present after reload.");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Delete_File_Preview_And_Apply_Removes_File_From_Isolated_Workspace_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var targetFilePath = Path.Combine(copiedRoot, "SampleLib", "Cat.cs");

            var preview = await FileOperationService.PreviewDeleteFileAsync(
                workspaceId,
                new DeleteFileDto(targetFilePath),
                CancellationToken.None);

            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase)));

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsFalse(File.Exists(targetFilePath));
            Assert.IsNull(SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspaceId), targetFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task File_Operation_Preview_Token_Is_Rejected_When_Workspace_Becomes_Stale()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var newFilePath = Path.Combine(copiedRoot, "SampleLib", "Generated", "StaleBird.cs");

            var preview = await FileOperationService.PreviewCreateFileAsync(
                workspaceId,
                new CreateFileDto("SampleLib", newFilePath, "namespace SampleLib.Generated;\n\npublic sealed class StaleBird { }\n"),
                CancellationToken.None);

            await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsFalse(applyResult.Success, "Stale previews should be rejected.");
            StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
            Assert.IsFalse(File.Exists(newFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Move_File_Preview_And_Apply_Updates_Isolated_Workspace_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var sourceFilePath = Path.Combine(copiedRoot, "SampleLib", "Dog.cs");
            var destinationFilePath = Path.Combine(copiedRoot, "SampleLib", "Animals", "Dog.cs");

            var preview = await FileOperationService.PreviewMoveFileAsync(
                workspaceId,
                new MoveFileDto(sourceFilePath, destinationFilePath, null, UpdateNamespace: true),
                CancellationToken.None);

            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase)));

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsFalse(File.Exists(sourceFilePath));
            Assert.IsTrue(File.Exists(destinationFilePath));
            var movedContents = await File.ReadAllTextAsync(destinationFilePath, CancellationToken.None);
            StringAssert.Contains(movedContents, "namespace SampleLib.Animals");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
