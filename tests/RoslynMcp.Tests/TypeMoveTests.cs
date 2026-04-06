namespace RoslynMcp.Tests;

[TestClass]
public sealed class TypeMoveTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task MoveType_FromMultiTypeFile_CreatesPreview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Add a second class to Cat.cs so it becomes a multi-type file before loading.
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var result = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PreviewToken));
    }

    [TestMethod]
    public async Task MoveType_SingleTypeFile_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "Dog", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task MoveType_NonExistentType_ThrowsInvalidOperation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "NonExistentType", null, CancellationToken.None));
    }
}
