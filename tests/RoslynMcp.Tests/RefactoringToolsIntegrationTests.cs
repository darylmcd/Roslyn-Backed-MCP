using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringToolsIntegrationTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task OrganizeUsings_Preview_Returns_Json()
    {
        var filePath = FindDocumentPath("Program.cs");
        var json = await RefactoringTools.PreviewOrganizeUsings(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            filePath,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("PreviewToken", out _));
    }

    [TestMethod]
    public async Task FormatDocument_Preview_Returns_PreviewToken()
    {
        var filePath = FindDocumentPath("Program.cs");
        var json = await RefactoringTools.PreviewFormatDocument(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            filePath,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("PreviewToken", out _));
    }

    [TestMethod]
    public async Task Rename_Preview_On_Method_Returns_PreviewToken()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await RefactoringTools.PreviewRename(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            newName: "GetAllAnimalsRenamed",
            filePath,
            line: 7,
            column: 26,
            symbolHandle: null,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("PreviewToken", out _));
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }
}
