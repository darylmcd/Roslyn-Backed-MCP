using System.Text.Json;
using RoslynMcp.Host.Stdio.Resources;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceResourceTests : SharedWorkspaceTestBase
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
    public void GetWorkspaceStatus_Resource_Returns_Json()
    {
        var json = WorkspaceResources.GetWorkspaceStatus(WorkspaceManager, WorkspaceId);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("ProjectCount").GetInt32() >= 1);
    }

    [TestMethod]
    public void GetProjects_Resource_Returns_Graph()
    {
        var json = WorkspaceResources.GetProjects(WorkspaceManager, WorkspaceId);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("Projects").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task GetDiagnostics_Resource_Returns_Result()
    {
        var json = await WorkspaceResources.GetDiagnostics(DiagnosticService, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("compilerDiagnostics", out _) || doc.RootElement.TryGetProperty("CompilerDiagnostics", out _));
    }

    [TestMethod]
    public async Task GetSourceFile_Resource_Returns_Text()
    {
        var path = FindDocumentPath("AnimalService.cs");
        var text = await WorkspaceResources.GetSourceFile(WorkspaceManager, WorkspaceId, path, CancellationToken.None);
        StringAssert.Contains(text, "GetAllAnimals");
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
