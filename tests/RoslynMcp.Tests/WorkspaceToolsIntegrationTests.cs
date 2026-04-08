using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceToolsIntegrationTests : SharedWorkspaceTestBase
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
    public async Task WorkspaceTools_ListWorkspaces_Returns_Count_And_Id()
    {
        var json = await WorkspaceTools.ListWorkspaces(WorkspaceManager);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 1);
        var workspaces = doc.RootElement.GetProperty("workspaces").EnumerateArray().ToList();
        Assert.IsTrue(workspaces.Any(w => w.GetProperty("WorkspaceId").GetString() == WorkspaceId));
    }

    [TestMethod]
    public async Task WorkspaceTools_GetWorkspaceStatus_Returns_Projects()
    {
        var json = await WorkspaceTools.GetWorkspaceStatus(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("ProjectCount").GetInt32() >= 1);
    }

    [TestMethod]
    public async Task WorkspaceTools_GetProjectGraph_Returns_Nodes()
    {
        var json = await WorkspaceTools.GetProjectGraph(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("Projects").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceGeneratedDocuments_Returns_Array()
    {
        var json = await WorkspaceTools.GetSourceGeneratedDocuments(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            projectName: null,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_Returns_LineCount()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("lineCount").GetInt32() >= 1);
        Assert.IsTrue(doc.RootElement.TryGetProperty("text", out _));
    }

    [TestMethod]
    public async Task WorkspaceExecutionGate_Rejects_Unknown_Workspace_Id()
    {
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            WorkspaceExecutionGate.RunReadAsync(
                "ffffffffffffffffffffffffffffffff",
                _ => Task.FromResult("x"),
                CancellationToken.None));
    }

    private static string FindProgramPath()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.Name, "Program.cs", StringComparison.Ordinal))?.FilePath;
        return path ?? throw new AssertFailedException("Program.cs not found.");
    }
}
