using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

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
public sealed class UndoFileOperationsTests : IsolatedWorkspaceTestBase
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
            DeleteDirectoryIfExists(solutionDir);
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
            DeleteDirectoryIfExists(solutionDir);
        }
    }

    [TestMethod]
    public async Task DocumentSetPersistence_MidBatchFailure_RestoresEveryPriorByte()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        AddProjectToCopiedSolution(solutionDir, "Contracts", "net10.0");
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var currentSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
            var dog = currentSolution.Projects
                .SelectMany(project => project.Documents)
                .Single(document => document.Name == "Dog.cs");
            var cat = currentSolution.Projects
                .SelectMany(project => project.Documents)
                .Single(document => document.Name == "Cat.cs");
            var dogPath = dog.FilePath ?? throw new AssertFailedException("Dog.cs path missing.");
            var catPath = cat.FilePath ?? throw new AssertFailedException("Cat.cs path missing.");
            var sampleLib = currentSolution.Projects.Single(project => project.Name == "SampleLib");
            var contracts = currentSolution.Projects.Single(project => project.Name == "Contracts");
            var sampleLibProjectPath = sampleLib.FilePath
                ?? throw new AssertFailedException("SampleLib.csproj path missing.");
            var dogBytes = await File.ReadAllBytesAsync(dogPath);
            var catBytes = await File.ReadAllBytesAsync(catPath);
            var projectBytes = await File.ReadAllBytesAsync(sampleLibProjectPath);

            var modifiedSolution = currentSolution
                .AddProjectReference(sampleLib.Id, new ProjectReference(contracts.Id))
                .WithDocumentText(dog.Id, SourceText.From("namespace SampleLib; public class Dog { }"))
                .WithDocumentText(cat.Id, SourceText.From("namespace SampleLib; public class Cat { }"));
            var service = new DocumentSetPersistenceService(
                WorkspaceManager,
                NullLogger.Instance,
                new FailThirdTextWriteFileSystem());

            var result = await service.PersistAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                modifiedSolution.GetChanges(currentSolution),
                CancellationToken.None);

            Assert.IsFalse(result.Success, "The injected second write must fail the transaction.");
            CollectionAssert.AreEqual(
                dogBytes,
                await File.ReadAllBytesAsync(dogPath),
                "The first successful write must be rolled back byte-for-byte.");
            CollectionAssert.AreEqual(
                catBytes,
                await File.ReadAllBytesAsync(catPath),
                "The failing file must retain its original bytes.");
            CollectionAssert.AreEqual(
                projectBytes,
                await File.ReadAllBytesAsync(sampleLibProjectPath),
                "A project-reference mutation written before the failure must be rolled back byte-for-byte.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(solutionDir);
        }
    }

    private sealed class FailThirdTextWriteFileSystem : IDocumentSetFileSystem
    {
        private int _textWriteCount;

        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteFile(string path) => File.Delete(path);
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) =>
            File.ReadAllBytesAsync(path, ct);
        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) =>
            File.ReadAllTextAsync(path, ct);
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct) =>
            File.WriteAllBytesAsync(path, bytes, ct);

        public Task WriteAllTextAsync(string path, string content, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _textWriteCount) == 3)
            {
                throw new IOException("Injected deterministic mid-batch write failure.");
            }

            return File.WriteAllTextAsync(path, content, ct);
        }
    }
}
