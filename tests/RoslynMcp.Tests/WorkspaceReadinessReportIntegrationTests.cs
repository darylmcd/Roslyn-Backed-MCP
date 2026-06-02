using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class WorkspaceReadinessReportIntegrationTests : IsolatedWorkspaceTestBase
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
    public async Task SampleSolution_ReadinessReport_ReturnsReadyVerdict()
    {
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("first-run-readiness", doc.RootElement.GetProperty("reportKind").GetString());
        Assert.AreEqual("ready", GetVerdict(doc));
        Assert.IsTrue(doc.RootElement.GetProperty("workspace").GetProperty("testProjectCount").GetInt32() >= 1);
        StringAssert.Contains(ReadLimitations(doc), "No tests");
    }

    [TestMethod]
    [TestCategory("RepoSolution")]
    public async Task RepoSolution_ReadinessReport_ReturnsOnboardingPayload()
    {
        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var loaded = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var json = await WorkspaceTools.GetWorkspaceReadinessReport(
                WorkspaceExecutionGate,
                WorkspaceManager,
                loaded.WorkspaceId,
                CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var workspace = doc.RootElement.GetProperty("workspace");
            Assert.AreEqual(loaded.WorkspaceId, workspace.GetProperty("workspaceId").GetString());
            Assert.IsTrue(workspace.GetProperty("projectCount").GetInt32() >= 1);
            Assert.IsTrue(IsKnownVerdict(workspace.GetProperty("verdict").GetString()));
            StringAssert.Contains(ReadLimitations(doc), "No tests");
        }
        finally
        {
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task UnresolvedAnalyzerFixture_ReadinessReport_ReturnsBuildNeededVerdict()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        InjectUnresolvedAnalyzerReference(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var workspaceId = await workspace.LoadAsync();
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            WorkspaceExecutionGate,
            WorkspaceManager,
            workspaceId,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("build-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "buildRequired=true");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet build");
    }

    private static bool IsKnownVerdict(string? verdict) =>
        verdict is "ready" or "restore-needed" or "build-needed" or "analyzer-limited";

    private static string GetVerdict(JsonDocument doc) =>
        doc.RootElement.GetProperty("workspace").GetProperty("verdict").GetString()!;

    private static string ReadLimitations(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("limitations").EnumerateArray().Select(value => value.GetString()));

    private static string ReadSignals(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("workspace").GetProperty("signals").EnumerateArray().Select(value => value.GetString()));

    private static string ReadWorkflows(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("nextRecommendedWorkflows").EnumerateArray().Select(value => value.GetProperty("workflow").GetString()));

    private static void InjectUnresolvedAnalyzerReference(string csprojPath)
    {
        var content = File.ReadAllText(csprojPath);
        var unresolvedAnalyzerSnippet = """
              <ItemGroup>
                <Analyzer Include="DefinitelyMissingAnalyzer.dll" />
              </ItemGroup>
            </Project>
            """;

        var newContent = content.Replace("</Project>", unresolvedAnalyzerSnippet, StringComparison.Ordinal);
        File.WriteAllText(csprojPath, newContent);
    }
}
