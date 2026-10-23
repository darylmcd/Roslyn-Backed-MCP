using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
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

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DocumentSetPersistence_ConflictingPhysicalContexts_RejectBeforeAnySourceEffect(bool removeSecond)
    {
        var directory = Path.Combine(Path.GetTempPath(), "physical-reference-conflict", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "A.csproj");
            var targetPath = Path.Combine(directory, "B.csproj");
            var documentPath = Path.Combine(directory, "Existing.cs");
            await File.WriteAllTextAsync(projectPath, "<Project><ItemGroup><ProjectReference Include=\"B.csproj\"><Aliases>old</Aliases></ProjectReference></ItemGroup></Project>");
            await File.WriteAllTextAsync(targetPath, "<Project />");
            await File.WriteAllTextAsync(documentPath, "class Original { }");
            using var workspace = new AdhocWorkspace();
            var first = ProjectId.CreateNewId();
            var second = ProjectId.CreateNewId();
            var target = ProjectId.CreateNewId();
            var document = DocumentId.CreateNewId(first);
            var original = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(first, VersionStamp.Create(), "A-first", "A", LanguageNames.CSharp, filePath: projectPath))
                .AddProject(ProjectInfo.Create(second, VersionStamp.Create(), "A-second", "A", LanguageNames.CSharp, filePath: projectPath))
                .AddProject(ProjectInfo.Create(target, VersionStamp.Create(), "B", "B", LanguageNames.CSharp, filePath: targetPath))
                .AddProjectReference(first, new ProjectReference(target, ["old"]))
                .AddProjectReference(second, new ProjectReference(target, ["old"]))
                .AddDocument(document, "Existing.cs", SourceText.From("class Original { }"), filePath: documentPath);
            var modified = original
                .WithProjectReferences(first, [new ProjectReference(target, ["first"])])
                .WithProjectReferences(second, removeSecond ? [] : [new ProjectReference(target, ["second"])])
                .WithDocumentText(document, SourceText.From("class Changed { }"));
            var projectBytes = await File.ReadAllBytesAsync(projectPath);
            var documentBytes = await File.ReadAllBytesAsync(documentPath);
            var fileSystem = new ObservingDocumentSetFileSystem();
            var service = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance, fileSystem);

            var result = await service.PersistAsync("conflicting-contexts", original, modified,
                modified.GetChanges(original), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, fileSystem.Effects, "Rollback is insufficient: contradictory plans must be rejected before any create, write or delete.");
            CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
            CollectionAssert.AreEqual(documentBytes, await File.ReadAllBytesAsync(documentPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ObservingDocumentSetFileSystem : IDocumentSetFileSystem
    {
        public int Effects { get; private set; }
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path)
        {
            Effects++;
            Directory.CreateDirectory(path);
        }
        public void DeleteFile(string path)
        {
            Effects++;
            File.Delete(path);
        }
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(path, ct);
        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => File.ReadAllTextAsync(path, ct);
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct)
        {
            Effects++;
            return File.WriteAllBytesAsync(path, bytes, ct);
        }
        public Task WriteAllTextAsync(string path, string content, CancellationToken ct)
        {
            Effects++;
            return File.WriteAllTextAsync(path, content, ct);
        }
    }

    [TestMethod]
    public async Task ProjectReferenceMetadata_PublicApplyMatchesUtf16Plan_AndUndoRestoresOriginalBytes()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Parse(await File.ReadAllTextAsync(projectPath), LoadOptions.PreserveWhitespace);
        xml.Root!.Add(new XElement("ItemGroup",
            new XElement("ProjectReference", new XAttribute("Include", "../Contracts/Contracts.csproj"),
                new XAttribute("Condition", "'$(TargetFramework)' == 'net10.0'"),
                new XElement("Aliases", new XAttribute("Condition", "'$(TargetFramework)' == 'net10.0'"),
                    new XAttribute("Label", "preserve-alias-label"), "old", new XComment("preserve-alias-comment")),
                new XElement("Private", new XAttribute("Label", "preserve-private-label"), "false"),
                new XComment("preserve-reference-comment"))));
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var originalBytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml.ToString(SaveOptions.DisableFormatting))).ToArray();
        await File.WriteAllBytesAsync(projectPath, originalBytes);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleLib");
        var target = original.Projects.Single(project => project.Name == "Contracts");
        var oldReference = source.ProjectReferences.Single(reference => reference.ProjectId == target.Id);
        CollectionAssert.AreEqual(new[] { "old" }, oldReference.Aliases.ToArray(), "The real MSBuild workspace must load the original reference metadata.");
        var modified = original.RemoveProjectReference(source.Id, oldReference)
            .AddProjectReference(source.Id, new ProjectReference(target.Id, ["reviewed"]));
        var planned = (await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
        var preview = await SolutionDiffHelper.ComputeChangesAsync(original, modified, CancellationToken.None);
        Assert.HasCount(1, preview.Where(change => change.FilePath == projectPath).ToArray());
        StringAssert.Contains(preview.Single(change => change.FilePath == projectPath).UnifiedDiff, "reviewed");
        var token = PreviewStore.Store(workspace.WorkspaceId, modified,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "replace reference metadata", false);
        var siblingDocument = source.Documents.Single(document => document.Name == "Dog.cs");
        var siblingPath = siblingDocument.FilePath!;
        var siblingText = (await siblingDocument.GetTextAsync()).ToString() + "\n// unrelated sibling apply must survive metadata replacement\n";
        var siblingSolution = original.WithDocumentText(siblingDocument.Id, SourceText.From(siblingText));
        var siblingToken = PreviewStore.Store(workspace.WorkspaceId, siblingSolution,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "unrelated sibling document edit", false);
        var siblingResult = await RefactoringService.ApplyRefactoringAsync(siblingToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(siblingResult.Success, siblingResult.Error);
        Assert.AreEqual(siblingText, await File.ReadAllTextAsync(siblingPath));
        Assert.AreNotSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId),
            "The metadata token must rebase after the successful public sibling apply.");

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(siblingText, await File.ReadAllTextAsync(siblingPath), "Metadata apply must preserve the sibling's source edit.");
        Assert.AreEqual(siblingText, (await WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId)
            .GetDocument(siblingDocument.Id)!.GetTextAsync()).ToString());
        var persistedBytes = await File.ReadAllBytesAsync(projectPath);
        CollectionAssert.AreEqual(planned.Modified, persistedBytes, "Public source apply must persist the exact bytes rendered for the preview.");
        var persisted = CsprojSemanticEquality.CreateSnapshot(persistedBytes);
        Assert.AreEqual(encoding.CodePage, persisted.TextEncoding.CodePage);
        Assert.IsTrue(persisted.HasPreamble);
        var reference = XDocument.Parse(persisted.Content).Descendants("ProjectReference").Single();
        Assert.AreEqual("'$(TargetFramework)' == 'net10.0'", (string?)reference.Attribute("Condition"));
        Assert.AreEqual("reviewed", reference.Element("Aliases")!.Value);
        Assert.AreEqual("'$(TargetFramework)' == 'net10.0'", (string?)reference.Element("Aliases")!.Attribute("Condition"));
        Assert.AreEqual("preserve-alias-label", (string?)reference.Element("Aliases")!.Attribute("Label"));
        Assert.AreEqual("false", reference.Element("Private")!.Value);
        Assert.AreEqual("preserve-private-label", (string?)reference.Element("Private")!.Attribute("Label"));
        CollectionAssert.AreEquivalent(new[] { "preserve-alias-comment", "preserve-reference-comment" },
            reference.DescendantNodes().OfType<XComment>().Select(comment => comment.Value).ToArray());

        Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId, CancellationToken.None));
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(projectPath),
            "Undo must restore the entire original project, including BOM, metadata and comments.");
        Assert.AreEqual(siblingText, await File.ReadAllTextAsync(siblingPath), "Undo of the metadata apply must preserve the earlier sibling apply.");
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeleteFile_Then_Revert_Restores_Original_Bytes(bool useUtf16)
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
            Encoding encoding = useUtf16
                ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var originalBytes = encoding.GetPreamble()
                .Concat(encoding.GetBytes(originalContent))
                .ToArray();
            await File.WriteAllBytesAsync(targetFilePath, originalBytes);

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
            CollectionAssert.AreEqual(
                originalBytes,
                await File.ReadAllBytesAsync(targetFilePath),
                "Restored file must exactly match the pre-apply byte sequence, including its BOM and encoding.");
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
            var addedPath = Path.Combine(
                Path.GetDirectoryName(sampleLibProjectPath)!,
                "TransactionalAddition.cs");
            var addedDocumentId = DocumentId.CreateNewId(sampleLib.Id, "TransactionalAddition.cs");
            var dogBytes = await File.ReadAllBytesAsync(dogPath);
            var catBytes = await File.ReadAllBytesAsync(catPath);
            var projectBytes = await File.ReadAllBytesAsync(sampleLibProjectPath);

            var modifiedSolution = currentSolution
                .AddProjectReference(sampleLib.Id, new ProjectReference(contracts.Id))
                .WithDocumentText(dog.Id, SourceText.From("namespace SampleLib; public class Dog { }"))
                .AddDocument(
                    addedDocumentId,
                    "TransactionalAddition.cs",
                    SourceText.From("namespace SampleLib; public class TransactionalAddition { }"),
                    filePath: addedPath)
                .RemoveDocument(cat.Id);
            var service = new DocumentSetPersistenceService(
                WorkspaceManager,
                NullLogger.Instance,
                new DeleteThenFailFileSystem(catPath));

            var result = await service.PersistAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                modifiedSolution.GetChanges(currentSolution),
                CancellationToken.None);

            Assert.IsFalse(result.Success, "The injected delete failure must fail the transaction.");
            CollectionAssert.AreEqual(
                dogBytes,
                await File.ReadAllBytesAsync(dogPath),
                "A changed document must be rolled back byte-for-byte.");
            CollectionAssert.AreEqual(
                catBytes,
                await File.ReadAllBytesAsync(catPath),
                "A document deleted before the injected failure must be recreated byte-for-byte.");
            Assert.IsFalse(
                File.Exists(addedPath),
                "A document created earlier in the failed transaction must be removed.");
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DocumentSetPersistence_ProjectReferenceWrite_PreservesEncoding(bool useUtf16)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        AddProjectToCopiedSolution(solutionDir, "Contracts", "net10.0");
        var sampleLibProjectPath = Path.Combine(solutionDir, "SampleLib", "SampleLib.csproj");
        Encoding encoding = useUtf16
            ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var projectText = await File.ReadAllTextAsync(sampleLibProjectPath);
        var encodedProject = encoding.GetPreamble()
            .Concat(encoding.GetBytes(projectText))
            .ToArray();
        await File.WriteAllBytesAsync(sampleLibProjectPath, encodedProject);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var currentSolution = WorkspaceManager.GetCurrentSolution(workspaceId);
            var sampleLib = currentSolution.Projects.Single(project => project.Name == "SampleLib");
            var contracts = currentSolution.Projects.Single(project => project.Name == "Contracts");
            var addedPath = Path.Combine(
                Path.GetDirectoryName(sampleLibProjectPath)!,
                "EncodingCompanion.cs");
            var addedDocumentId = DocumentId.CreateNewId(sampleLib.Id, "EncodingCompanion.cs");
            var modifiedSolution = currentSolution.AddProjectReference(
                sampleLib.Id,
                new ProjectReference(contracts.Id))
                .AddDocument(
                    addedDocumentId,
                    "EncodingCompanion.cs",
                    SourceText.From("namespace SampleLib; public sealed class EncodingCompanion { }"),
                    filePath: addedPath);
            var service = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance);

            var result = await service.PersistAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                modifiedSolution.GetChanges(currentSolution),
                CancellationToken.None);

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
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(solutionDir);
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
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void DeleteFile(string path)
        {
            File.Delete(path);
            if (string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(failingDeletePath),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new IOException("Injected deterministic failure after document deletion.");
            }
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) =>
            File.ReadAllBytesAsync(path, ct);
        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) =>
            File.ReadAllTextAsync(path, ct);
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct) =>
            File.WriteAllBytesAsync(path, bytes, ct);
        public Task WriteAllTextAsync(string path, string content, CancellationToken ct) =>
            File.WriteAllTextAsync(path, content, ct);
    }
}
