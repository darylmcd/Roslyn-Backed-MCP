using System.Text.Json;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;

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
    public async Task GetWorkspaceStatus_Resource_Returns_Json()
    {
        var json = await WorkspaceResources.GetWorkspaceStatus(WorkspaceManager, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("projectCount").GetInt32() >= 1);
    }

    [TestMethod]
    public async Task GetWorkspaceStatus_Resource_Default_Is_Summary()
    {
        var json = await WorkspaceResources.GetWorkspaceStatus(WorkspaceManager, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("projects", out _),
            "Default workspace_status resource must return the summary shape (no per-project tree).");
        Assert.IsTrue(doc.RootElement.TryGetProperty("workspaceDiagnosticCount", out _),
            "Summary shape must include WorkspaceDiagnosticCount.");
    }

    [TestMethod]
    public async Task GetWorkspaceStatusVerbose_Resource_Includes_Projects()
    {
        var json = await WorkspaceResources.GetWorkspaceStatusVerbose(WorkspaceManager, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("projects", out var projects),
            "Verbose workspace_status resource must include the per-project tree.");
        Assert.IsTrue(projects.GetArrayLength() >= 1);
    }

    [TestMethod]
    public void GetWorkspaces_Resource_Default_Is_Summary()
    {
        var json = WorkspaceResources.GetWorkspaces(WorkspaceManager);
        using var doc = JsonDocument.Parse(json);
        var workspaces = doc.RootElement.GetProperty("workspaces").EnumerateArray().ToList();
        Assert.IsTrue(workspaces.Count >= 1);
        Assert.IsFalse(workspaces[0].TryGetProperty("projects", out _),
            "Default workspaces resource must return summary shape per workspace.");
    }

    [TestMethod]
    public void GetWorkspacesVerbose_Resource_Includes_Projects()
    {
        var json = WorkspaceResources.GetWorkspacesVerbose(WorkspaceManager);
        using var doc = JsonDocument.Parse(json);
        var workspaces = doc.RootElement.GetProperty("workspaces").EnumerateArray().ToList();
        Assert.IsTrue(workspaces.Count >= 1);
        Assert.IsTrue(workspaces[0].TryGetProperty("projects", out _),
            "Verbose workspaces resource must include per-project trees.");
    }

    [TestMethod]
    public void GetProjects_Resource_Returns_Graph()
    {
        var json = WorkspaceResources.GetProjects(WorkspaceManager, WorkspaceId);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("projects").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task GetDiagnostics_Resource_Returns_Result()
    {
        var json = await WorkspaceResources.GetDiagnostics(DiagnosticService, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("compilerDiagnostics", out _));
    }

    [TestMethod]
    public async Task GetDiagnostics_Resource_AppliesSeverityFloorAndCap()
    {
        // diagnostics-resource-timeout: the resource caps at 500 entries and applies a Warning
        // floor by default. Verify the new envelope includes severityFloor + cap + hasMore so
        // callers know to switch to project_diagnostics for fuller scopes.
        var json = await WorkspaceResources.GetDiagnostics(DiagnosticService, WorkspaceId, CancellationToken.None);
        using var doc = JsonDocument.Parse(json);

        Assert.IsTrue(doc.RootElement.TryGetProperty("severityFloor", out var floor));
        Assert.AreEqual("Warning", floor.GetString());

        Assert.IsTrue(doc.RootElement.TryGetProperty("cap", out var cap));
        Assert.AreEqual(500, cap.GetInt32());

        Assert.IsTrue(doc.RootElement.TryGetProperty("hasMore", out var hasMore));
        Assert.IsTrue(hasMore.ValueKind == JsonValueKind.True || hasMore.ValueKind == JsonValueKind.False,
            "hasMore must be a boolean.");

        Assert.IsTrue(doc.RootElement.TryGetProperty("returnedDiagnostics", out var returned));
        Assert.IsTrue(returned.GetInt32() <= 500, "Resource must enforce the 500-row cap.");
    }

    [TestMethod]
    public async Task GetSourceFile_Resource_Returns_Text()
    {
        var path = FindDocumentPath("AnimalService.cs");
        var text = await WorkspaceResources.GetSourceFile(WorkspaceManager, WorkspaceId, path, CancellationToken.None);
        StringAssert.Contains(text, "GetAllAnimals");
    }

    [TestMethod]
    public async Task GetSourceFile_Resource_Accepts_Uri_Encoded_Absolute_Path()
    {
        var path = FindDocumentPath("AnimalService.cs");
        var encoded = Uri.EscapeDataString(path);
        var text = await WorkspaceResources.GetSourceFile(WorkspaceManager, WorkspaceId, encoded, CancellationToken.None);
        StringAssert.Contains(text, "GetAllAnimals");
    }

    [TestMethod]
    public async Task GetSourceFile_Resource_Accepts_Forward_Slashes_On_Windows()
    {
        var path = FindDocumentPath("AnimalService.cs");
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Forward-slash drive paths are Windows-specific.");
            return;
        }

        var forward = path.Replace('\\', '/');
        var text = await WorkspaceResources.GetSourceFile(WorkspaceManager, WorkspaceId, forward, CancellationToken.None);
        StringAssert.Contains(text, "GetAllAnimals");
    }

    [TestMethod]
    public async Task GetSourceFile_Resource_Rejects_Relative_Path()
    {
        var ex = await Assert.ThrowsExceptionAsync<McpToolException>(() =>
            WorkspaceResources.GetSourceFile(WorkspaceManager, WorkspaceId, "AnimalService.cs", CancellationToken.None));
        StringAssert.Contains(ex.Message, "Invalid operation");
        StringAssert.Contains(ex.Message, "absolute path");
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
