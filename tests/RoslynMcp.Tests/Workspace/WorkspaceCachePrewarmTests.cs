using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests.Workspace;

/// <summary>
/// Regression coverage for <c>workspace-cache-prewarm-on-load</c>: callers can opt into the
/// warm-start profile directly on <c>workspace_load</c>, while the default load path stays cold.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceCachePrewarmTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task LoadWorkspace_WithPrewarmTrue_WarmsWorkspaceAndReturnsWarmSummary()
    {
        await using var isolated = CreateIsolatedWorkspaceCopy();
        string? workspaceId = null;

        try
        {
            var json = await WorkspaceTools.LoadWorkspace(
                server: null!,
                gate: WorkspaceExecutionGate,
                workspace: WorkspaceManager,
                warmService: WorkspaceWarmService,
                commandRunner: DotnetCommandRunner,
                path: isolated.SolutionPath,
                verbose: false,
                autoRestore: false,
                prewarm: true,
                progress: null,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            workspaceId = root.GetProperty("workspaceId").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId), "workspace_load must return a workspaceId.");

            Assert.IsTrue(root.TryGetProperty("prewarm", out var prewarm),
                "prewarm=true must return the workspace_warm result block alongside the load summary.");
            Assert.AreEqual(workspaceId, prewarm.GetProperty("workspaceId").GetString(),
                "The prewarm result must target the workspace that workspace_load just returned.");
            Assert.IsTrue(prewarm.GetProperty("projectsWarmed").GetArrayLength() > 0,
                "Prewarm should visit at least one project in the sample solution.");
            Assert.IsTrue(prewarm.GetProperty("coldCompilationCount").GetInt32() > 0,
                "Prewarm should pay the cold compilation cost on a fresh isolated workspace.");

            var repeatWarm = await WorkspaceWarmService.WarmAsync(workspaceId!, projects: null, CancellationToken.None);
            Assert.AreEqual(0, repeatWarm.ColdCompilationCount,
                "After workspace_load(prewarm=true), a repeat warm should find every project already warm.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                WorkspaceManager.Close(workspaceId);
            }
        }
    }

    [TestMethod]
    public async Task LoadWorkspace_DefaultPrewarmFalse_DoesNotWarmWorkspace()
    {
        await using var isolated = CreateIsolatedWorkspaceCopy();
        string? workspaceId = null;

        try
        {
            var json = await WorkspaceTools.LoadWorkspace(
                server: null!,
                gate: WorkspaceExecutionGate,
                workspace: WorkspaceManager,
                warmService: WorkspaceWarmService,
                commandRunner: DotnetCommandRunner,
                path: isolated.SolutionPath,
                verbose: false,
                autoRestore: false,
                prewarm: false,
                progress: null,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            workspaceId = root.GetProperty("workspaceId").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId), "workspace_load must return a workspaceId.");
            Assert.IsFalse(root.TryGetProperty("prewarm", out _),
                "The default cold-load response must not include a prewarm block.");

            var firstWarmAfterLoad = await WorkspaceWarmService.WarmAsync(workspaceId!, projects: null, CancellationToken.None);
            Assert.IsTrue(firstWarmAfterLoad.ColdCompilationCount > 0,
                "prewarm=false must preserve the cold workspace profile until the caller explicitly warms.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                WorkspaceManager.Close(workspaceId);
            }
        }
    }
}
