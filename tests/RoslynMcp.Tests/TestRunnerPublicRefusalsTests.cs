using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestRunnerPublicRefusalsTests : SharedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-TEST-RUN-REFUSAL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false, "workspace_status with verbose=true")]
    [DataRow(true, "test SDK")]
    public async Task PrepareRefusals_PublishCorrectionWithoutCallerValues(bool noProjects, string correction)
    {
        var project = new ProjectStatusDto(Sentinel, Path.Combine(TestTempRoot.Current, Sentinel + ".csproj"),
            0, [], ["net10.0"], false, Sentinel, "Library");
        var status = new WorkspaceStatusDto(Sentinel, project.FilePath, 1, "snapshot", DateTimeOffset.UtcNow,
            1, 0, [project], true, false, []);
        var manager = new FailClosedWorkspaceManagerStub { GetStatusAsyncHandler = _ => status };
        using var executor = new ProjectOnlyExecutor(project);
        var runner = new TestRunnerService(manager, executor, NullLogger<TestRunnerService>.Instance, TestDiscoveryService);
        var exception = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            runner.PrepareTestRunAsync(Sentinel, noProjects ? null : Sentinel, null, CancellationToken.None));
        var json = ToolErrorHandler.ClassifyAndFormat(exception, "test_run");
        using var envelope = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", envelope.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!, correction);
        Assert.IsFalse(json.Contains(Sentinel, StringComparison.Ordinal), json);
        Assert.IsFalse(json.Contains("PublicInvalidOperationException", StringComparison.Ordinal), json);
    }

    [TestMethod]
    [DataRow("2025-11-25", false, "workspace_status with verbose=true")]
    [DataRow("2026-07-28", false, "workspace_status with verbose=true")]
    [DataRow("2025-11-25", true, "test SDK")]
    [DataRow("2026-07-28", true, "test SDK")]
    public async Task TestRun_RealServiceRefusal_PreservesRawWireContract(string era, bool noProjects, string correction)
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("test-run-public-refusals", era);
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var solution = noProjects
            ? Path.Combine(root, "samples", "BuildFailureSolution", "BuildFailureSolution.slnx")
            : SampleSolutionPath;
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = solution, ["autoRestore"] = false });
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        foreach (var verbose in new[] { false, true })
        {
            var status = await harness.Client.CallToolAsync("workspace_status",
                new Dictionary<string, object?> { ["workspaceId"] = id, ["verbose"] = verbose });
            Assert.IsFalse(status.IsError == true);
            var payload = JsonNode.Parse(status.Content.OfType<TextContentBlock>().Single().Text)!.AsObject();
            Assert.AreEqual(verbose, payload.ContainsKey("projects"), "The refusal remedy must actually expose project names.");
            if (verbose)
                Assert.IsTrue(payload["projects"]!.AsArray().All(project => !string.IsNullOrWhiteSpace(project!["name"]!.GetValue<string>())));
        }
        var arguments = new Dictionary<string, object?> { ["workspaceId"] = id };
        if (!noProjects) arguments["projectName"] = "SampleLib";
        var prior = harness.RawServerMessages.Count;
        var response = await harness.Client.CallToolAsync("test_run", arguments);
        Assert.IsTrue(response.IsError == true);
        var frame = harness.RawServerMessages.Skip(prior).Select(raw => JsonNode.Parse(raw)!.AsObject())
            .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
        Assert.IsNull(frame["error"]);
        var result = frame["result"]!.AsObject();
        Assert.AreEqual(true, result["isError"]!.GetValue<bool>());
        Assert.AreEqual(era == "2026-07-28" ? "complete" : null, result["resultType"]?.GetValue<string>());
        Assert.HasCount(1, result["content"]!.AsArray());
        var envelope = JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
        Assert.AreEqual("InvalidOperation", envelope["category"]!.GetValue<string>());
        Assert.AreEqual("InvalidOperationException", envelope["exceptionType"]!.GetValue<string>());
        Assert.IsNotNull(envelope["_meta"]);
        StringAssert.Contains(envelope["schemaHint"]!.GetValue<string>(), "test_run(");
        StringAssert.Contains(envelope["message"]!.GetValue<string>(), correction);
        var rawFrame = frame.ToJsonString();
        Assert.IsFalse(rawFrame.Contains(id, StringComparison.Ordinal), rawFrame);
        Assert.IsFalse(rawFrame.Contains(JsonSerializer.Serialize(root).Trim('"'), StringComparison.Ordinal), rawFrame);
    }

    private sealed class ProjectOnlyExecutor(ProjectStatusDto project) : IGatedCommandExecutor
    {
        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) => project;
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath,
            IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct) =>
            throw new AssertFailedException("A refused request must not execute a process.");
        public void Dispose() { }
    }
}
