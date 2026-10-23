using RoslynMcp.Tests.Helpers;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
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

    private static async Task<IReadOnlyList<FileSnapshotDto>> CaptureFilesAsync(Solution before, Solution after)
    {
        var snapshots = new List<FileSnapshotDto>();
        foreach (var path in SolutionFileMutationPlanner.ChangedPaths(before, after))
            snapshots.Add(File.Exists(path) ? FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))
                : new FileSnapshotDto(path, null));
        return snapshots;
    }

    [TestMethod]
    public async Task CreateFile_Then_Revert_Removes_File_From_Disk()
    {
        // SampleSolution audit §9.13 verbatim behavior: before the fix, AnimalSpeaker.cs
        // remained on disk as an untracked file after revert_last_apply.
        var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var solutionDir = workspace.RootPath;
        var workspaceId = workspace.WorkspaceId;
        Exception? primary = null;
        try
        {
            var newFilePath = Path.Combine(solutionDir, "SampleLib", "Item2Guard_CreatedOnly.cs");
            Assert.IsFalse(File.Exists(newFilePath), "Sanity: target file must not pre-exist.");
            var previewDto = await FileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(ProjectName: "SampleLib", FilePath: newFilePath, Content: "namespace SampleLib;\npublic static class Item2Guard_CreatedOnly { }\n"), CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "Apply must succeed as a precondition.");
            Assert.IsTrue(File.Exists(newFilePath), "File must be on disk after apply.");
            var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
            Assert.IsTrue(reverted, "revert_last_apply must report success.");
            Assert.IsFalse(File.Exists(newFilePath), "Files created by the apply MUST be deleted on revert. Before Item #2 this file persisted on disk as untracked content.");
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("File undo workspace cleanup failed.", primary, workspace.DisposeAsync);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeleteFile_Then_Revert_Restores_Original_Bytes(bool useUtf16)
    {
        var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var solutionDir = workspace.RootPath;
        var workspaceId = workspace.WorkspaceId;
        Exception? primary = null;
        try
        {
            // Create a throwaway file first so we have a deterministic target to delete.
            var targetFilePath = Path.Combine(solutionDir, "SampleLib", "Item2Guard_ToDelete.cs");
            const string originalContent = "namespace SampleLib;\npublic static class Item2Guard_ToDelete { public const int Marker = 42; }\n";
            Encoding encoding = useUtf16 ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true) : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var originalBytes = encoding.GetPreamble().Concat(encoding.GetBytes(originalContent)).ToArray();
            await File.WriteAllBytesAsync(targetFilePath, originalBytes);
            // Reload so the workspace sees the new file as part of its solution.
            await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
            var previewDto = await FileOperationService.PreviewDeleteFileAsync(workspaceId, new DeleteFileDto(targetFilePath), CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "Delete apply must succeed as a precondition.");
            Assert.IsFalse(File.Exists(targetFilePath), "File must be gone from disk after delete apply.");
            var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
            Assert.IsTrue(reverted, "revert_last_apply must report success.");
            Assert.IsTrue(File.Exists(targetFilePath), "Files deleted by the apply MUST be restored on revert.");
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(targetFilePath), "Restored file must exactly match the pre-apply byte sequence, including its BOM and encoding.");
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("File undo workspace cleanup failed.", primary, workspace.DisposeAsync);
        }
    }

    [TestMethod]
    public async Task DocumentSetPersistence_MidBatchFailure_RestoresEveryPriorByte()
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        Exception? resourceFailure19 = null;
        try
        {
            var copiedSolutionPath = workspace.SolutionPath;
            var solutionDir = workspace.RootPath;
            AddProjectToCopiedSolution(solutionDir, "Contracts", "net10.0");
            await RestoreCopiedSolutionAsync(copiedSolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            var workspaceId = await workspace.LoadAsync(CancellationToken.None);
            var currentSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
            var dog = currentSolution.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Dog.cs");
            var cat = currentSolution.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Cat.cs");
            var dogPath = dog.FilePath ?? throw new AssertFailedException("Dog.cs path missing.");
            var catPath = cat.FilePath ?? throw new AssertFailedException("Cat.cs path missing.");
            var sampleLib = currentSolution.Projects.Single(project => project.Name == "SampleLib");
            var contracts = currentSolution.Projects.Single(project => project.Name == "Contracts");
            var sampleLibProjectPath = sampleLib.FilePath ?? throw new AssertFailedException("SampleLib.csproj path missing.");
            var addedPath = Path.Combine(Path.GetDirectoryName(sampleLibProjectPath)!, "TransactionalAddition.cs");
            var addedDocumentId = DocumentId.CreateNewId(sampleLib.Id, "TransactionalAddition.cs");
            var dogBytes = await File.ReadAllBytesAsync(dogPath);
            var catBytes = await File.ReadAllBytesAsync(catPath);
            var projectBytes = await File.ReadAllBytesAsync(sampleLibProjectPath);
            var modifiedSolution = currentSolution.AddProjectReference(sampleLib.Id, new ProjectReference(contracts.Id)).WithDocumentText(dog.Id, SourceText.From("namespace SampleLib; public class Dog { }")).AddDocument(addedDocumentId, "TransactionalAddition.cs", SourceText.From("namespace SampleLib; public class TransactionalAddition { }"), filePath: addedPath).RemoveDocument(cat.Id);
            var service = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance, new DeleteThenFailFileSystem(catPath));
            var files = await CaptureFilesAsync(currentSolution, modifiedSolution);
            var physical = await ((IWorkspaceRecoveryAuthority)WorkspaceManager).CaptureRecoveryAsync(workspaceId, "persistence test", files, CancellationToken.None);
            var result = await service.PersistAsync(workspaceId, currentSolution, modifiedSolution, modifiedSolution.GetChanges(currentSolution), files, physical, CancellationToken.None);
            Assert.IsFalse(result.Success, "The injected delete failure must fail the transaction.");
            CollectionAssert.AreEqual(dogBytes, await File.ReadAllBytesAsync(dogPath), "A changed document must be rolled back byte-for-byte.");
            CollectionAssert.AreEqual(catBytes, await File.ReadAllBytesAsync(catPath), "A document deleted before the injected failure must be recreated byte-for-byte.");
            Assert.IsFalse(File.Exists(addedPath), "A document created earlier in the failed transaction must be removed.");
            CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(sampleLibProjectPath), "A project-reference mutation written before the failure must be rolled back byte-for-byte.");
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure19 = cleanupFailure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native workspace resource cleanup failed.", resourceFailure19, workspace.DisposeAsync);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DocumentSetPersistence_ProjectReferenceWrite_PreservesEncoding(bool useUtf16)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        Exception? resourceFailure20 = null;
        try
        {
            var copiedSolutionPath = workspace.SolutionPath;
            var solutionDir = workspace.RootPath;
            AddProjectToCopiedSolution(solutionDir, "Contracts", "net10.0");
            var sampleLibProjectPath = Path.Combine(solutionDir, "SampleLib", "SampleLib.csproj");
            Encoding encoding = useUtf16 ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true) : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var projectText = await File.ReadAllTextAsync(sampleLibProjectPath);
            var encodedProject = encoding.GetPreamble().Concat(encoding.GetBytes(projectText)).ToArray();
            await File.WriteAllBytesAsync(sampleLibProjectPath, encodedProject);
            await RestoreCopiedSolutionAsync(copiedSolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            var workspaceId = await workspace.LoadAsync(CancellationToken.None);
            var currentSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
            var sampleLib = currentSolution.Projects.Single(project => project.Name == "SampleLib");
            var contracts = currentSolution.Projects.Single(project => project.Name == "Contracts");
            var addedPath = Path.Combine(Path.GetDirectoryName(sampleLibProjectPath)!, "EncodingCompanion.cs");
            var addedDocumentId = DocumentId.CreateNewId(sampleLib.Id, "EncodingCompanion.cs");
            var modifiedSolution = currentSolution.AddProjectReference(sampleLib.Id, new ProjectReference(contracts.Id)).AddDocument(addedDocumentId, "EncodingCompanion.cs", SourceText.From("namespace SampleLib; public sealed class EncodingCompanion { }"), filePath: addedPath);
            var service = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance);
            var files = await CaptureFilesAsync(currentSolution, modifiedSolution);
            var physical = await ((IWorkspaceRecoveryAuthority)WorkspaceManager).CaptureRecoveryAsync(workspaceId, "persistence test", files, CancellationToken.None);
            var result = await service.PersistAsync(workspaceId, currentSolution, modifiedSolution, modifiedSolution.GetChanges(currentSolution), files, physical, CancellationToken.None);
            Assert.IsTrue(result.Success);
            CollectionAssert.Contains(result.AppliedFiles.ToList(), sampleLibProjectPath);
            var persistedBytes = await File.ReadAllBytesAsync(sampleLibProjectPath);
            var persistedSnapshot = CsprojSemanticEquality.CreateSnapshot(persistedBytes);
            Assert.AreEqual(encoding.CodePage, persistedSnapshot.TextEncoding.CodePage);
            Assert.IsTrue(persistedSnapshot.HasPreamble);
            StringAssert.Contains(persistedSnapshot.Content, "ProjectReference");
            StringAssert.Contains(persistedSnapshot.Content, "Contracts.csproj");
            Assert.IsTrue(File.Exists(addedPath));
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure20 = cleanupFailure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native workspace resource cleanup failed.", resourceFailure20, workspace.DisposeAsync);
        }
    }

    [TestMethod]
    public void ProjectReferenceTargetsPath_DistinguishesSameNamedProjects()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", "App");
        var firstTarget = Path.Combine(Path.GetTempPath(), "RoslynMcp", "One", "Common.csproj");

        Assert.IsTrue(DocumentSetPersistenceService.ProjectReferenceTargetsPath(
            Path.Combine("..", "One", "Common.csproj"),
            projectDirectory,
            firstTarget));
        Assert.IsFalse(DocumentSetPersistenceService.ProjectReferenceTargetsPath(
            Path.Combine("..", "Two", "Common.csproj"),
            projectDirectory,
            firstTarget));
    }

    private sealed class DeleteThenFailFileSystem(string failingDeletePath) : IDocumentSetFileSystem
    {
        public async Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct)
            => FileContentDigest.FromBytes(await ReadAllBytesAsync(path, ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The required file reader returned no content.");

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(path, ct);
        public async Task TransitionAsync(string path, byte[]? expected, byte[]? desired, PhysicalMutationRecovery operation,
            IWorkspaceRecoveryAuthority authority, CancellationToken ct)
        {
            await operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
            if (desired is null && FileSystemPath.Comparer.Equals(Path.GetFullPath(path), Path.GetFullPath(failingDeletePath)))
                throw new IOException("Injected deterministic failure after document deletion.");
        }
    }
}
