using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ValidationToolsIntegrationTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task TestDiscover_Returns_Projects()
    {
        var json = await ValidationTools.DiscoverTests(
            WorkspaceExecutionGate,
            TestDiscoveryService,
            WorkspaceId,
            projectName: null,
            limit: 50,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("testProjects", out var tp) || doc.RootElement.TryGetProperty("TestProjects", out tp));
    }

    [TestMethod]
    public async Task BuildProject_SampleLib_Returns_Result()
    {
        var json = await ValidationTools.BuildProject(
            WorkspaceExecutionGate,
            BuildService,
            WorkspaceId,
            "SampleLib",
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("Execution", out var exec));
        Assert.IsTrue(exec.TryGetProperty("Succeeded", out var ok) && ok.GetBoolean());
    }

    [TestMethod]
    public async Task CompileCheck_Service_GetDiagnostics_Succeeds_For_SampleSolution()
    {
        var result = await CompileCheckService.CheckAsync(WorkspaceId, projectFilter: null, emitValidation: false, CancellationToken.None);
        Assert.IsTrue(result.Success, "Sample solution should compile without errors.");
        Assert.AreEqual(0, result.ErrorCount);
        Assert.IsNotNull(result.Diagnostics);
    }

    [TestMethod]
    public async Task CompileCheck_Service_EmitValidation_Completes_For_SampleLib()
    {
        var result = await CompileCheckService.CheckAsync(WorkspaceId, projectFilter: "SampleLib", emitValidation: true, CancellationToken.None);
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.ElapsedMs >= 0);
    }

    [TestMethod]
    public async Task CompileCheck_Tool_Returns_Valid_Json()
    {
        var json = await CompileCheckTools.CompileCheck(
            WorkspaceExecutionGate,
            CompileCheckService,
            WorkspaceId,
            project: null,
            emitValidation: false,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.IsTrue(
            root.TryGetProperty("Success", out _) || root.TryGetProperty("success", out _),
            "Expected Success in JSON output.");
    }

    [TestMethod]
    public async Task TestRelated_Returns_Json_For_File()
    {
        var programPath = FindDocumentPath("Program.cs");
        var json = await ValidationTools.FindRelatedTests(
            WorkspaceExecutionGate,
            TestDiscoveryService,
            WorkspaceId,
            programPath,
            line: 1,
            column: 1,
            symbolHandle: null,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsNotNull(doc.RootElement);
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
