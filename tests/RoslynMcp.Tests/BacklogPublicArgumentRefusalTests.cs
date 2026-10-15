using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BacklogPublicArgumentRefusalTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-ARGUMENT-REFUSAL-SENTINEL";
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
    [DataRow("handle-empty", "symbolHandle", "empty", "ArgumentException")]
    [DataRow("handle-base64", "symbolHandle", "base64", "ArgumentException")]
    [DataRow("handle-json", "symbolHandle", "JSON", "ArgumentException")]
    [DataRow("handle-null", "symbolHandle", "JSON", "ArgumentException")]
    [DataRow("handle-payload", "symbolHandle", "MetadataName", "ArgumentException")]
    [DataRow("bulk-cap", "symbols", "50", "ArgumentException")]
    [DataRow("relationship-project", "projectName", "workspace_status", "ArgumentException")]
    [DataRow("consumer-name", "typeName", "non-empty", "ArgumentException")]
    [DataRow("consumer-limit", "limit", ">= 1", "ArgumentException")]
    [DataRow("msbuild-property", "projectName", "\"projectName\": \"MyApp.Core\"", "ArgumentException")]
    [DataRow("msbuild-items", "projectName", "\"projectName\": \"MyApp.Core\"", "ArgumentException")]
    [DataRow("related-cap", "filePaths", "maximum of", "ArgumentException")]
    [DataRow("unused-kind", "usageKind", "never-read, never-written, or never-either", "ArgumentException")]
    [DataRow("dead-empty", "request", "At least one symbol handle", "ArgumentException")]
    [DataRow("snippet-kind", "kind", "'expression', 'statements', 'returnExpression', 'members', or 'program'", "ArgumentException")]
    [DataRow("script-zero", "timeoutSecondsOverride", "greater than zero", "ArgumentOutOfRangeException")]
    [DataRow("script-cap", "timeoutSecondsOverride", "4294967", "ArgumentOutOfRangeException")]
    [DataRow("script-ipc-code", "request", "1048576", "ArgumentException")]
    [DataRow("script-ipc-imports", "request", "1048576", "ArgumentException")]
    [DataRow("fixall-scope", "scope", "'document', 'project', or 'solution'", "ArgumentException")]
    [DataRow("fixall-file", "filePath", "required when scope is 'document'", "ArgumentException")]
    [DataRow("fixall-project", "projectName", "workspace_status", "ArgumentException")]
    [DataRow("data-start", "startLine", "must be >= 1", "ArgumentException")]
    [DataRow("data-end", "endLine", "must be >= 1", "ArgumentException")]
    [DataRow("data-order", "startLine", "must be <= endLine", "ArgumentException")]
    [DataRow("control-start", "startLine", "must be >= 1", "ArgumentException")]
    [DataRow("control-end", "endLine", "must be >= 1", "ArgumentException")]
    [DataRow("control-order", "startLine", "must be <= endLine", "ArgumentException")]
    public async Task AuthoredGuards_PublishCorrectionsThroughProductionEnvelope(
        string scenario, string parameter, string correction, string identity)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => InvokeGuardAsync(scenario));
        Assert.IsInstanceOfType<IPublicMessageException>(exception, "Caller-correctable guards must explicitly own their public text.");
        Assert.AreEqual(parameter, exception.ParamName);
        var json = ToolErrorHandler.ClassifyAndFormat(exception, "argument_refusal_probe");
        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;
        Assert.IsTrue(envelope.GetProperty("error").GetBoolean(), json);
        Assert.AreEqual("InvalidArgument", envelope.GetProperty("category").GetString(), json);
        Assert.AreEqual(identity, envelope.GetProperty("exceptionType").GetString(), json);
        StringAssert.Contains(envelope.GetProperty("message").GetString()!, correction);
        Assert.IsFalse(json.Contains(Sentinel, StringComparison.Ordinal), json);
        Assert.IsFalse(json.Contains("PublicArgument", StringComparison.Ordinal), json);
        Assert.IsFalse(json.Contains("stackTrace", StringComparison.Ordinal), json);
    }

    private static async Task InvokeGuardAsync(string scenario)
    {
        switch (scenario)
        {
            case "handle-empty":
            case "handle-base64":
            case "handle-json":
            case "handle-null":
            case "handle-payload":
                var handle = scenario switch
                {
                    "handle-empty" => " ",
                    "handle-base64" => Sentinel + "!",
                    "handle-json" => Convert.ToBase64String(Encoding.UTF8.GetBytes("{" + Sentinel)),
                    "handle-null" => Convert.ToBase64String(Encoding.UTF8.GetBytes("null")),
                    _ => Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
                };
                _ = SymbolHandleSerializer.ParseHandlePayload(handle);
                break;
            case "bulk-cap":
                await ReferenceService.FindReferencesBulkAsync(WorkspaceId,
                    Enumerable.Repeat(new BulkSymbolLocator(Sentinel, null, null, null, null), 51).ToArray(),
                    false, CancellationToken.None);
                break;
            case "relationship-project":
                await SymbolRelationshipService.FindOverloadsAsync(WorkspaceId, "SampleLib.Dog", "Talk", false, Sentinel, CancellationToken.None);
                break;
            case "consumer-name":
            case "consumer-limit":
                await TypeConsumersService.FindTypeConsumersAsync(WorkspaceId, scenario == "consumer-name" ? " " : Sentinel,
                    scenario == "consumer-limit" ? 0 : 1, CancellationToken.None);
                break;
            case "msbuild-property":
                await MsBuildEvaluationService.EvaluatePropertyAsync(WorkspaceId, " ", Sentinel, CancellationToken.None);
                break;
            case "msbuild-items":
                await MsBuildEvaluationService.EvaluateItemsAsync(WorkspaceId, " ", Sentinel, CancellationToken.None);
                break;
            case "related-cap":
                await TestDiscoveryService.FindRelatedTestsForFilesAsync(WorkspaceId,
                    Enumerable.Repeat(Sentinel, ValidationOptions.MaxRelatedFiles + 1).ToArray(), 50, CancellationToken.None);
                break;
            case "unused-kind":
                await UnusedCodeAnalyzer.FindDeadFieldsAsync(WorkspaceId, new DeadFieldsAnalysisOptions { UsageKindFilter = Sentinel }, CancellationToken.None);
                break;
            case "dead-empty":
                await DeadCodeService.PreviewRemoveDeadCodeAsync(WorkspaceId, new DeadCodeRemovalDto([]), CancellationToken.None);
                break;
            case "snippet-kind":
                await SnippetAnalysisService.AnalyzeAsync(Sentinel, null, Sentinel, CancellationToken.None);
                break;
            case "script-zero":
            case "script-cap":
                var scripting = new ScriptingService(NullLogger<ScriptingService>.Instance,
                    new ScriptingServiceOptions { WatchdogGraceSeconds = 0 });
                await scripting.EvaluateAsync(Sentinel, null, CancellationToken.None,
                    onProgress: null, timeoutSecondsOverride: scenario == "script-zero" ? 0 : int.MaxValue);
                break;
            case "script-ipc-code":
            case "script-ipc-imports":
                var oversized = Sentinel + new string('x', ScriptWorkerProcess.MaxIpcCharacters);
                await new ScriptWorkerProcess(NullLogger.Instance).StartAsync(new ScriptWorkerRequest(
                    scenario == "script-ipc-code" ? oversized : "0",
                    scenario == "script-ipc-imports" ? [oversized] : null, 1), CancellationToken.None);
                break;
            case "fixall-scope":
                _ = FixAllTargetResolver.ParseAndValidate(Sentinel, Sentinel, Sentinel);
                break;
            case "fixall-file":
                _ = FixAllTargetResolver.ParseAndValidate("document", " ", Sentinel);
                break;
            case "fixall-project":
                _ = FixAllTargetResolver.ParseAndValidate("project", Sentinel, " ");
                break;
            default:
                var start = scenario.EndsWith("start", StringComparison.Ordinal) ? 0
                    : scenario.EndsWith("order", StringComparison.Ordinal) ? 2 : 1;
                var end = scenario.EndsWith("end", StringComparison.Ordinal) ? 0 : 1;
                if (scenario.StartsWith("data-", StringComparison.Ordinal))
                    await FlowAnalysisService.AnalyzeDataFlowAsync(WorkspaceId, Sentinel, start, end, CancellationToken.None);
                else if (scenario.StartsWith("control-", StringComparison.Ordinal))
                    await FlowAnalysisService.AnalyzeControlFlowAsync(WorkspaceId, Sentinel, start, end, CancellationToken.None);
                else
                    Assert.Fail("Unknown regression scenario: " + scenario);
                break;
        }
    }

    [TestMethod]
    [DataRow("budget")]
    [DataRow("deadline")]
    [DataRow("heartbeat")]
    public async Task SupervisorSettings_AreInternalFailuresBeforeStartingWorker(string invalid)
    {
        var supervisor = new ScriptExecutionSupervisor(NullLogger.Instance, new ScriptingServiceOptions());
        var settings = new ScriptExecutionSupervisorSettings(1, 0, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        settings = invalid switch
        {
            "budget" => settings with { Budget = TimeSpan.Zero },
            "deadline" => settings with { HardDeadlineSeconds = 0 },
            _ => settings with { HeartbeatInterval = TimeSpan.Zero },
        };
        var invoked = false;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => supervisor.ExecuteAsync(
            _ => { invoked = true; return ScriptExecutionOutcome.Success(0); }, null, settings, CancellationToken.None));
        Assert.IsFalse(invoked);
        Assert.IsFalse(exception is IPublicMessageException);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(int.MaxValue)]
    public async Task ConfiguredDefaultScriptTimeout_IsInternalAndNeverStartsWorker(int configuredTimeout)
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { TimeoutSeconds = configuredTimeout, WatchdogGraceSeconds = 0 });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(
            Sentinel, null, CancellationToken.None, onProgress: null, timeoutSecondsOverride: null));
        Assert.IsFalse(exception is IPublicMessageException);
        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [DataRow("build_project")]
    [DataRow("test_run")]
    [DataRow("rename_preview")]
    [DataRow("fix_all_preview")]
    [DataRow("evaluate_msbuild_property")]
    public async Task MissingTargets_KeepStableWireIdentityAndActionableAdvice(string tool)
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("missing-target-refusals", "2026-07-28");
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = SampleSolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var arguments = new Dictionary<string, object?> { ["workspaceId"] = id };
        if (tool == "rename_preview")
        {
            arguments["symbolHandle"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { MetadataName = Sentinel })));
            arguments["newName"] = "Renamed";
        }
        else
        {
            arguments["projectName"] = Sentinel;
            if (tool == "fix_all_preview")
            {
                arguments["scope"] = "project";
                arguments["diagnosticId"] = "CA1822";
            }
            if (tool == "evaluate_msbuild_property") arguments["propertyName"] = "TargetFramework";
        }
        var result = await harness.Client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);
        Assert.IsTrue(result.IsError == true);
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;
        Assert.AreEqual("InvalidOperation", envelope.GetProperty("category").GetString(), json);
        Assert.AreEqual("InvalidOperationException", envelope.GetProperty("exceptionType").GetString(), json);
        var message = envelope.GetProperty("message").GetString()!;
        Assert.IsFalse(message.Contains("workspace_reload", StringComparison.Ordinal), json);
        if (tool == "rename_preview") StringAssert.Contains(message, "symbolHandle");
        else
        {
            StringAssert.Contains(message, "Loaded projects");
            StringAssert.Contains(message, "SampleLib");
        }
        Assert.IsFalse(json.Contains(Sentinel, StringComparison.Ordinal), json);
        Assert.IsFalse(json.Contains(id, StringComparison.Ordinal), json);
    }
}
