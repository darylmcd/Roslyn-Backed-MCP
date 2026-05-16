using System.Text.Json;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class IntegrationTests_WorkspaceCore : SharedWorkspaceTestBase
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
    public void Workspace_Load_Returns_WorkspaceId_And_Metadata()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        Assert.IsTrue(status.IsLoaded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.WorkspaceId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.SnapshotToken));
        Assert.AreEqual(SampleSolutionPath, status.LoadedPath);
        Assert.IsTrue(status.ProjectCount >= 2);
        Assert.IsTrue(status.DocumentCount >= 1);
        Assert.IsTrue(status.Projects.Count >= 2, $"Expected at least 2 projects, got {status.Projects.Count}");
    }

    [TestMethod]
    public void Workspace_Status_Can_Be_Looked_Up_By_Id()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        Assert.AreEqual(WorkspaceId, status.WorkspaceId);
    }

    [TestMethod]
    public void Workspace_Status_Serializes_Project_Name_As_Name_JsonKey()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var json = JsonSerializer.Serialize(status);

        StringAssert.Contains(json, "\"name\":");
    }

    [TestMethod]
    public async Task Workspace_Reload_Rejects_Unknown_Id()
    {
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            WorkspaceManager.ReloadAsync("missing-workspace", CancellationToken.None));
    }

    [TestMethod]
    public void Workspace_Has_SampleLib_Project()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var sampleLib = status.Projects.FirstOrDefault(p => p.Name == "SampleLib");
        Assert.IsNotNull(sampleLib, "SampleLib project not found");
        Assert.IsTrue(sampleLib.DocumentCount > 0);
        CollectionAssert.Contains(sampleLib.TargetFrameworks.ToList(), "net10.0");
    }

    [TestMethod]
    public void Workspace_Has_SampleApp_Project()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var sampleApp = status.Projects.FirstOrDefault(p => p.Name == "SampleApp");
        Assert.IsNotNull(sampleApp, "SampleApp project not found");
        Assert.IsTrue(sampleApp.ProjectReferences.Contains("SampleLib"));
    }

    [TestMethod]
    public void Workspace_Status_And_Project_Graph_Resolve_TargetFrameworks_From_DirectoryBuildProps()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var sampleTestsStatus = status.Projects.First(project => project.Name == "SampleLib.Tests");
        CollectionAssert.Contains(sampleTestsStatus.TargetFrameworks.ToList(), "net10.0");

        var graph = WorkspaceManager.GetProjectGraph(WorkspaceId);
        var sampleTestsGraph = graph.Projects.First(project => project.ProjectName == "SampleLib.Tests");
        CollectionAssert.Contains(sampleTestsGraph.TargetFrameworks.ToList(), "net10.0");
    }

    [TestMethod]
    public void Project_Graph_Serializes_Project_Node_With_Name_JsonKey()
    {
        var graph = WorkspaceManager.GetProjectGraph(WorkspaceId);
        var json = JsonSerializer.Serialize(graph);
        StringAssert.Contains(json, "\"name\":");
    }
}
