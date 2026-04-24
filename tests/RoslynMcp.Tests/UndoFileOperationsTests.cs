using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #2 — regression guard for `severity-high-fail-documented-semantic-is-restore-pr`
/// and its granular siblings (`dr-9-3-leaves-files-created-by-the-apply-on-disk`,
/// `dr-9-13-does-not-delete-files-created-by-the-reverted-a`). The
/// RefactoringService apply path now captures an authoritative FileSnapshotDto list
/// alongside the Solution snapshot so UndoService takes its fast path
/// (RevertFromFileSnapshotsAsync) and restores disk state for file create/delete/move.
/// </summary>
[TestClass]
public sealed class UndoFileOperationsTests : TestBase
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
    public async Task CreateFile_Then_Revert_Removes_File_From_Disk()
    {
        // SampleSolution audit §9.13 verbatim behavior: before the fix, AnimalSpeaker.cs
        // remained on disk as an untracked file after revert_last_apply.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var newFilePath = Path.Combine(solutionDir, "SampleLib", "Item2Guard_CreatedOnly.cs");
            Assert.IsFalse(File.Exists(newFilePath), "Sanity: target file must not pre-exist.");

            var previewDto = await FileOperationService.PreviewCreateFileAsync(
                workspaceId,
                new CreateFileDto(
                    ProjectName: "SampleLib",
                    FilePath: newFilePath,
                    Content: "namespace SampleLib;\npublic static class Item2Guard_CreatedOnly { }\n"),
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "Apply must succeed as a precondition.");
            Assert.IsTrue(File.Exists(newFilePath), "File must be on disk after apply.");

            var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);

            Assert.IsTrue(reverted, "revert_last_apply must report success.");
            Assert.IsFalse(
                File.Exists(newFilePath),
                "Files created by the apply MUST be deleted on revert. Before Item #2 this file persisted on disk as untracked content.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task DeleteFile_Then_Revert_Restores_File_With_Original_Content()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Create a throwaway file first so we have a deterministic target to delete.
            var targetFilePath = Path.Combine(solutionDir, "SampleLib", "Item2Guard_ToDelete.cs");
            const string originalContent = "namespace SampleLib;\npublic static class Item2Guard_ToDelete { public const int Marker = 42; }\n";
            await File.WriteAllTextAsync(targetFilePath, originalContent);

            // Reload so the workspace sees the new file as part of its solution.
            await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

            var previewDto = await FileOperationService.PreviewDeleteFileAsync(
                workspaceId,
                new DeleteFileDto(targetFilePath),
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "Delete apply must succeed as a precondition.");
            Assert.IsFalse(File.Exists(targetFilePath), "File must be gone from disk after delete apply.");

            var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);

            Assert.IsTrue(reverted, "revert_last_apply must report success.");
            Assert.IsTrue(
                File.Exists(targetFilePath),
                "Files deleted by the apply MUST be restored on revert.");
            var restoredContent = await File.ReadAllTextAsync(targetFilePath);
            Assert.AreEqual(
                originalContent,
                restoredContent,
                "Restored file must exactly match the pre-apply content.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
