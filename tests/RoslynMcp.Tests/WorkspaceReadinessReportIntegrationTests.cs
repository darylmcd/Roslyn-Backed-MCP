using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class WorkspaceReadinessReportIntegrationTests : IsolatedWorkspaceTestBase
{
    private static readonly TimeSpan ReadinessBuildTimeout = TimeSpan.FromSeconds(90);

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [TestCategory("Process")]
    public async Task SampleSolution_ReadinessReport_ReturnsReadyVerdict()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await BuildWorkspaceFixtureAsync(workspace, CancellationToken.None);
        var workspaceId = await workspace.LoadAsync();
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            WorkspaceExecutionGate,
            WorkspaceManager,
            workspaceId,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json.TextPayload());
        var status = await WorkspaceManager.GetStatusAsync(workspaceId, CancellationToken.None);
        var diagnosticSummary = string.Join(
            Environment.NewLine,
            status.WorkspaceDiagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
        Assert.AreEqual("first-run-readiness", doc.RootElement.GetProperty("reportKind").GetString());
        Assert.AreEqual(
            "ready",
            GetVerdict(doc),
            doc.RootElement + Environment.NewLine + diagnosticSummary);
        Assert.IsTrue(doc.RootElement.GetProperty("workspace").GetProperty("testProjectCount").GetInt32() >= 1);
        StringAssert.Contains(ReadLimitations(doc), "No tests");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task SampleSolutionWithoutAssets_ReadinessReport_ReturnsRestoreNeededVerdict()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        RemoveBuildArtifacts(workspace.RootPath);
        InitializeGitFixture(workspace.RootPath);

        var workspaceId = await workspace.LoadAsync();
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            WorkspaceExecutionGate,
            WorkspaceManager,
            workspaceId,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("restore-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "restoreRequired=true");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet restore");
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

            using var doc = JsonDocument.Parse(json.TextPayload());
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
    [TestCategory("Process")]
    public async Task UnresolvedAnalyzerFixture_ReadinessReport_ReturnsBuildNeededVerdict()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await BuildWorkspaceFixtureAsync(workspace, CancellationToken.None);
        InjectUnresolvedAnalyzerReference(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var workspaceId = await workspace.LoadAsync();
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            WorkspaceExecutionGate,
            WorkspaceManager,
            workspaceId,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("build-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "buildRequired=true");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet build");
    }

    private static async Task BuildWorkspaceFixtureAsync(IsolatedWorkspaceScope workspace, CancellationToken ct)
    {
        RemoveBuildArtifacts(workspace.RootPath);
        InitializeGitFixture(workspace.RootPath);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ReadinessBuildTimeout);
        CommandExecutionDto execution;
        try
        {
            execution = await DotnetCommandRunner.RunAsync(
                workingDirectory: workspace.RootPath,
                targetPath: workspace.SolutionPath,
                arguments: ["build", workspace.SolutionPath, "--nologo"],
                timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"dotnet build did not complete within {ReadinessBuildTimeout.TotalSeconds:F0} seconds for readiness fixture '{workspace.SolutionPath}'.",
                ex);
        }

        Assert.IsTrue(
            execution.Succeeded,
            $"dotnet build failed for readiness fixture. ExitCode={execution.ExitCode} StdOut={execution.StdOut} StdErr={execution.StdErr}");
    }

    private static void RemoveBuildArtifacts(string rootPath)
    {
        var buildDirectories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "bin" or "obj")
            .OrderByDescending(path => path.Length)
            .ToArray();
        foreach (var path in buildDirectories)
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(path);
        }
    }

    private static void InitializeGitFixture(string rootPath)
    {
        if (!GitFixtureRunner.IsAvailable(out var failureReason))
        {
            Assert.Inconclusive($"git is required to exercise the SourceLink-ready fixture: {failureReason}");
        }

        GitFixtureRunner.InitializeRepository(rootPath, "https://example.invalid/fixture.git");
        GitFixtureRunner.StageAndCommitAll(rootPath);
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
