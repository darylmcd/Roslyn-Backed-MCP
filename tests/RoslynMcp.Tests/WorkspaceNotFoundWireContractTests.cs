using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// workspace-id-unknown-error-category (4.x additive form): raw-wire pins for an unknown or
/// never-loaded <c>workspaceId</c>. Within the 4.x line the envelope keeps the v4.2.1 values —
/// <c>category: "NotFound"</c> and <c>exceptionType: "KeyNotFoundException"</c> — and adds the
/// optional <c>reason: "WorkspaceNotFound"</c> discriminator, which symbol, file, and metadata
/// misses never carry. <c>resources/read</c> keeps the v4.2.1 <c>NotFound</c> category prefix and
/// era-correct not-found code, and carries the same reason token in the sanitized message.
/// </summary>
[TestClass]
public sealed class WorkspaceNotFoundWireContractTests
{
    private const string _legacyProtocolVersion = "2025-11-25";
    private const string _modernProtocolVersion = "2026-07-28";
    private const string _unknownWorkspaceId = "ffffffffffffffffffffffffffffffff";

    private static readonly (string? Requested, string Expected, int NotFoundCode)[] _protocolEras =
    [
        (_legacyProtocolVersion, _legacyProtocolVersion, -32002),
        (null, _modernProtocolVersion, -32602),
    ];

    [TestMethod]
    public async Task UnknownWorkspaceId_ToolErrors_KeepNotFoundAndAddWorkspaceReason_AcrossProtocolEras()
    {
        foreach (var protocol in _protocolEras)
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            Assert.AreEqual(protocol.Expected, harness.Client.NegotiatedProtocolVersion);

            // compile_check: the execution gate's ContainsWorkspace precheck.
            var compile = await CallAndReadErrorEnvelopeAsync(
                harness, "compile_check", Args(("workspaceId", _unknownWorkspaceId)), protocol.Expected);
            AssertWorkspaceMissEnvelope(compile, "compile_check", protocol.Expected);
            Assert.IsNull(compile["schemaHint"],
                $"[{protocol.Expected}] compile_check hints only on InvalidArgument. Envelope: {compile.ToJsonString()}");

            // test_run: a validation tool that hints on every category, so the reason must
            // coexist with schemaHint instead of displacing it.
            var testRun = await CallAndReadErrorEnvelopeAsync(
                harness, "test_run", Args(("workspaceId", _unknownWorkspaceId)), protocol.Expected);
            AssertWorkspaceMissEnvelope(testRun, "test_run", protocol.Expected);
            StringAssert.Contains(testRun["schemaHint"]?.GetValue<string>(), "test_run(",
                $"[{protocol.Expected}] test_run must keep its any-category schemaHint. Envelope: {testRun.ToJsonString()}");
        }
    }

    [TestMethod]
    public async Task UnknownWorkspaceId_ResourceRead_KeepsNotFoundPrefixAndCarriesWorkspaceReason_AcrossProtocolEras()
    {
        foreach (var protocol in _protocolEras)
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            var label = $"[{protocol.Expected}] workspace_status resource";
            var before = harness.RawServerMessages.Count;

            await Assert.ThrowsAsync<McpException>(async () =>
                _ = await harness.Client.ReadResourceAsync(
                    new ReadResourceRequestParams { Uri = $"roslyn://workspace/{_unknownWorkspaceId}/status" },
                    CancellationToken.None));

            var frame = FindSingleNewFrame(harness.RawServerMessages, before, label);
            Assert.IsTrue(frame.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject,
                $"{label}: a failed read must answer on the JSON-RPC error channel. Frame: {frame.ToJsonString()}");
            var error = (JsonObject)errorNode!;
            Assert.AreEqual(protocol.NotFoundCode, error["code"]!.GetValue<int>(),
                $"{label}: wrong not-found code. Frame: {frame.ToJsonString()}");

            var message = error["message"]!.GetValue<string>();
            Assert.IsTrue(message.StartsWith("NotFound: ", StringComparison.Ordinal),
                $"{label}: the 4.x category prefix must stay NotFound. Message: '{message}'");
            StringAssert.Contains(message, "reason: WorkspaceNotFound", $"{label}: message '{message}'");
            Assert.IsFalse(message.Contains("Exception", StringComparison.Ordinal),
                $"{label}: resource errors never carry exception type names. Message: '{message}'");
        }
    }

    [TestMethod]
    public async Task SymbolMiss_OnLoadedWorkspace_StaysNotFoundWithoutWorkspaceReason()
    {
        MsBuildInitializer.EnsureInitialized();
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var sampleSolution = TestFixtureFileSystem.FindFixturePath(
            repositoryRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");

        await using var harness = await CreateHarnessAsync(protocolVersion: null);
        var load = await harness.Client.CallToolAsync(
            "workspace_load",
            Args(("path", sampleSolution), ("prewarm", false), ("autoRestore", false)),
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(load.IsError is true, $"workspace_load failed: {load.TextPayload()}");
        using var loadDocument = JsonDocument.Parse(load.TextPayload());
        var workspaceId = loadDocument.RootElement.GetProperty("workspaceId").GetString()!;

        var envelope = await CallAndReadErrorEnvelopeAsync(
            harness,
            "find_references",
            Args(("workspaceId", workspaceId), ("metadataName", "SampleLib.NoSuchTypeForWorkspaceReasonWireTest")),
            _modernProtocolVersion);

        Assert.AreEqual("NotFound", envelope["category"]?.GetValue<string>(), envelope.ToJsonString());
        Assert.IsFalse(envelope.ContainsKey("reason"),
            $"Only a workspace miss carries the reason discriminator; a symbol miss must not. Envelope: {envelope.ToJsonString()}");
    }

    private static void AssertWorkspaceMissEnvelope(JsonObject envelope, string toolName, string protocol)
    {
        var label = $"[{protocol}] {toolName}";
        var json = envelope.ToJsonString();
        Assert.AreEqual("NotFound", envelope["category"]?.GetValue<string>(),
            $"{label}: an unknown workspaceId keeps the 4.x NotFound category. Envelope: {json}");
        Assert.AreEqual("WorkspaceNotFound", envelope["reason"]?.GetValue<string>(),
            $"{label}: the additive reason discriminator must identify the workspace miss. Envelope: {json}");
        Assert.AreEqual("KeyNotFoundException", envelope["exceptionType"]?.GetValue<string>(),
            $"{label}: exceptionType keeps its 4.x value. Envelope: {json}");
        Assert.AreEqual(toolName, envelope["tool"]?.GetValue<string>(), label);
        StringAssert.Contains(envelope["message"]?.GetValue<string>(), "workspace_load",
            $"{label}: the remediation must name the recovery tool. Envelope: {json}");
    }

    private static async Task<JsonObject> CallAndReadErrorEnvelopeAsync(
        InMemoryMcpClientServerHarness harness,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string protocol)
    {
        var label = $"[{protocol}] {toolName}";
        var before = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(toolName, arguments, cancellationToken: CancellationToken.None);
        Assert.IsTrue(result.IsError, $"{label}: expected isError=true, got: {result.TextPayload()}");

        var frame = FindSingleNewFrame(harness.RawServerMessages, before, label);
        Assert.IsNull(frame["error"], $"{label}: tool failures answer in result, not the JSON-RPC error channel.");
        var wireResult = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        Assert.AreEqual(true, wireResult["isError"]?.GetValue<bool>(), $"{label}: raw frame must carry isError=true.");
        var envelope = Assert.IsInstanceOfType<JsonObject>(
            JsonNode.Parse(wireResult["content"]![0]!["text"]!.GetValue<string>()));
        Assert.AreEqual(true, envelope["error"]?.GetValue<bool>(), label);
        Assert.IsNotNull(envelope["_meta"], $"{label}: the shared filter must inject _meta.");
        return envelope;
    }

    private static JsonObject FindSingleNewFrame(IReadOnlyList<string> rawMessages, int skip, string label)
    {
        var frames = rawMessages.Skip(skip)
            .Select(static raw => JsonNode.Parse(raw))
            .OfType<JsonObject>()
            .Where(static message => message.ContainsKey("id") && (message["result"] is not null || message["error"] is not null))
            .ToList();
        Assert.HasCount(1, frames, $"{label}: expected exactly one new response frame.");
        return frames[0];
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values) =>
        values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(string? protocolVersion) =>
        ProductionParityMcpHarness.CreateAsync(
            $"workspace-not-found-wire-{protocolVersion ?? _modernProtocolVersion}",
            protocolVersion);
}
