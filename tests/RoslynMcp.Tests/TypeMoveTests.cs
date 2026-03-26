namespace RoslynMcp.Tests;

[TestClass]
public sealed class TypeMoveTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task MoveType_FromMultiTypeFile_CreatesPreview()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            // Add a second class to Cat.cs so it becomes a multi-type file
            var catDir = Path.GetDirectoryName(copyPath)!;
            var catFile = Path.Combine(catDir, "SampleLib", "Cat.cs");
            File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

            var result = await TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            CleanupTempDirectory(copyPath);
        }
    }

    [TestMethod]
    public async Task MoveType_SingleTypeFile_ThrowsInvalidOperation()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("Dog.cs") == true);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                TypeMoveService.PreviewMoveTypeToFileAsync(
                    wsId, doc.FilePath!, "Dog", null, CancellationToken.None));

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            CleanupTempDirectory(copyPath);
        }
    }

    [TestMethod]
    public async Task MoveType_NonExistentType_ThrowsInvalidOperation()
    {
        var copyPath = CreateSampleSolutionCopy();
        try
        {
            var status = await WorkspaceManager.LoadAsync(copyPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var doc = WorkspaceManager.GetCurrentSolution(wsId)
                .Projects.SelectMany(p => p.Documents)
                .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                TypeMoveService.PreviewMoveTypeToFileAsync(
                    wsId, doc.FilePath!, "NonExistentType", null, CancellationToken.None));

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
