namespace RoslynMcp.Tests;

[TestClass]
public sealed class TypeExtractionTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ExtractType_FromAnimalService_CreatesPreview()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

            var result = await TypeExtractionService.PreviewExtractTypeAsync(
                wsId, doc.FilePath!, "AnimalService", ["CountAnimals"], "AnimalCounter", null, CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
            Assert.IsTrue(result.Description?.Contains("AnimalCounter") == true,
                $"Description should mention AnimalCounter, got: {result.Description}");

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            CleanupTempDirectory(copyPath);
        }
    }

    [TestMethod]
    public async Task ExtractType_EmptyMemberList_ThrowsArgument()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, doc.FilePath!, "AnimalService", [], "NewType", null, CancellationToken.None));

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            CleanupTempDirectory(copyPath);
        }
    }

    [TestMethod]
    public async Task ExtractType_NonExistentType_ThrowsInvalidOperation()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("AnimalService.cs") == true);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    wsId, doc.FilePath!, "NonExistentType", ["Foo"], "NewType", null, CancellationToken.None));

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            CleanupTempDirectory(copyPath);
        }
    }

    private static void CleanupTempDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
