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

    [TestMethod]
    public async Task MoveType_EnumDeclaration_ThrowsStructuredTypeKindError()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Append an enum so Cat.cs becomes a multi-type file (a sibling class is required by
        // the single-type guard) but the *target* of the move is the enum.
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic enum IngestionTerminalStatus\n{\n    Pending,\n    Completed,\n    Failed,\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "IngestionTerminalStatus", null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "type-kind Enum not supported",
            $"Expected structured type-kind error citing Enum; got: {ex.Message}");
        // Must NOT degrade into the misleading "not found" wording.
        Assert.IsFalse(ex.Message.Contains("not found", StringComparison.Ordinal),
            $"Enum should be reported as unsupported kind, not 'not found': {ex.Message}");
    }

    [TestMethod]
    public async Task MoveType_DelegateDeclaration_ThrowsStructuredTypeKindError()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic delegate void NotificationHandler(string message);\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            TypeMoveService.PreviewMoveTypeToFileAsync(
                wsId, doc.FilePath!, "NotificationHandler", null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "type-kind Delegate not supported",
            $"Expected structured type-kind error citing Delegate; got: {ex.Message}");
        Assert.IsFalse(ex.Message.Contains("not found", StringComparison.Ordinal),
            $"Delegate should be reported as unsupported kind, not 'not found': {ex.Message}");
    }

    [TestMethod]
    public async Task MoveType_NewFile_HasNoLeadingBlankLine()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var preview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);

        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);

        var newFilePath = workspace.GetPath("SampleLib", "Kitten.cs");
        Assert.IsTrue(File.Exists(newFilePath), "Kitten.cs should have been created.");

        var content = await File.ReadAllTextAsync(newFilePath, CancellationToken.None);
        var normalized = content.Replace("\r\n", "\n");

        Assert.IsFalse(normalized.StartsWith('\n'),
            $"New file must not start with a blank line. Actual head: {normalized[..Math.Min(80, normalized.Length)]}");
    }

    [TestMethod]
    public async Task MoveType_NewFile_NoStrayBlankLineRuns()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        File.AppendAllText(catFile, "\npublic class Kitten : IAnimal\n{\n    public string Name => \"Kitten\";\n    public string Speak() => \"Mew\";\n}\n");

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Cat.cs") == true);

        var preview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            wsId, doc.FilePath!, "Kitten", null, CancellationToken.None);
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);

        var content = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "Kitten.cs"), CancellationToken.None);
        var normalized = content.Replace("\r\n", "\n");

        // Two blank lines in a row would manifest as three consecutive newlines.
        Assert.IsFalse(normalized.Contains("\n\n\n", StringComparison.Ordinal),
            "New file should not contain runs of 2+ blank lines (three consecutive newlines).");
    }
}
