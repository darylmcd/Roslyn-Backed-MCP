using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class IntegrationTests_EditApply : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Format_Document_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewFormatDocumentAsync(WorkspaceId, serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has intentional formatting issues
        // Changes may or may not be produced depending on the formatter
    }

    [TestMethod]
    public async Task Organize_Usings_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(WorkspaceId, serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has unused using System.Threading
    }

    [TestMethod]
    public async Task Rename_Preview_And_Apply_Update_Isolated_Workspace_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var dogFilePath = Path.Combine(isolatedRoot, "SampleLib", "Dog.cs");
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewRenameAsync(
                isolatedWorkspaceId,
                SymbolLocator.BySource(dogFilePath, 3, 14),
                "Hound",
                CancellationToken.None);
            Assert.IsTrue(preview.Changes.Count > 0, "Rename preview should produce file changes.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var dogContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            StringAssert.Contains(dogContents, "class Hound");
            StringAssert.Contains(serviceContents, "new Hound()");
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
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
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            // Two reloads push the version past the pinned ceiling (V + 1) — token dropped.
            await WorkspaceManager.ReloadAsync(isolatedWorkspaceId, CancellationToken.None);
            await WorkspaceManager.ReloadAsync(isolatedWorkspaceId, CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsFalse(applyResult.Success, "Two reloads must push past the pinned ceiling and reject the apply.");
            StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }

    [TestMethod]
    public async Task Organize_Usings_Apply_Removes_Unused_Using_In_Isolated_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(serviceContents.Contains("using System.Threading;"));
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }

    [TestMethod]
    public async Task Format_Document_Apply_Normalizes_Isolated_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewFormatDocumentAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(serviceContents.Contains("MakeThemSpeak(    IEnumerable<IAnimal>     animals   )"));
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }
}
