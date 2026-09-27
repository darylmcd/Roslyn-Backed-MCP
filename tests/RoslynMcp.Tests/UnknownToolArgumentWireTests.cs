using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class UnknownToolArgumentWireTests
{
    private const string ReadToolName = "compile_check";
    private const string WriteToolName = "workspace_close";

    [TestMethod]
    public async Task MisspelledReadArgument_IsReportedWithSuggestion_AndNotBound()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, ReadToolName, new()
        {
            ["workspaceId"] = "ws-1",
            ["severty"] = "Error",
        });

        Assert.IsFalse(result.IsError is true);
        var payload = ParseTextContent(result);
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("severity").ValueKind,
            "The SDK binder drops the misspelled key; the detector must only report it.");
        AssertSingleUnknown(payload, "severty", "severity");
    }

    [TestMethod]
    public async Task MisspelledWriteArgument_IsReportedWithSuggestion()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, WriteToolName, new()
        {
            ["workspaceId"] = "ws-1",
            ["drainProceses"] = true,
        });

        Assert.IsFalse(result.IsError is true);
        var payload = ParseTextContent(result);
        Assert.IsFalse(payload.GetProperty("drainProcesses").GetBoolean());
        AssertSingleUnknown(payload, "drainProceses", "drainProcesses");
    }

    [TestMethod]
    public async Task UnknownArgument_IsReportedOnErrorEnvelope()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, WriteToolName, new()
        {
            ["workspaceId"] = "missing",
            ["drainProceses"] = true,
        });

        Assert.IsTrue(result.IsError is true);
        AssertSingleUnknown(ParseTextContent(result), "drainProceses", "drainProcesses");
    }

    [TestMethod]
    public async Task CleanCall_HasNoUnknownArgumentsKey()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, ReadToolName, new()
        {
            ["workspaceId"] = "ws-1",
            ["severity"] = "Error",
        });

        var payload = ParseTextContent(result);
        Assert.AreEqual("Error", payload.GetProperty("severity").GetString());
        Assert.IsFalse(payload.GetProperty("_meta").TryGetProperty("unknownArguments", out _));
    }

    [TestMethod]
    public async Task ProgressTokenInRequestMeta_IsNotReported()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await harness.Client.CallToolAsync(
            ReadToolName,
            new Dictionary<string, object?> { ["workspaceId"] = "ws-1" },
            progress: new Progress<ProgressNotificationValue>(static _ => { }),
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(result.IsError is true);
        Assert.IsFalse(ParseTextContent(result).GetProperty("_meta").TryGetProperty("unknownArguments", out _),
            "progressToken travels in params._meta, not params.arguments.");
    }

    [TestMethod]
    public async Task CaseVariantKey_IsNotBound_AndIsReported()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, ReadToolName, new()
        {
            ["workspaceId"] = "ws-1",
            ["Severity"] = "Error",
        });

        var payload = ParseTextContent(result);
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("severity").ValueKind,
            "The SDK binder matches names ordinally, so the detector compares ordinally too.");
        AssertSingleUnknown(payload, "Severity", "severity");
    }

    [TestMethod]
    public async Task DistantUnknownArgument_IsReportedWithoutSuggestion()
    {
        await using var harness = await CreateHarnessAsync();

        var result = await CallAsync(harness, ReadToolName, new()
        {
            ["workspaceId"] = "ws-1",
            ["verbosityLevel"] = 3,
        });

        var entries = ParseTextContent(result).GetProperty("_meta").GetProperty("unknownArguments");
        Assert.AreEqual(1, entries.GetArrayLength());
        Assert.AreEqual("verbosityLevel", entries[0].GetProperty("name").GetString());
        Assert.IsFalse(entries[0].TryGetProperty("suggestion", out _));
    }

    [TestMethod]
    public void SchemaWithoutProperties_YieldsNoFindings()
    {
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        var declared = UnknownArgumentDetector.ReadDeclaredNames(schema.RootElement);

        Assert.IsNull(UnknownArgumentDetector.Detect(["anything"], declared));
    }

    [TestMethod]
    [DataRow("severty", "severity", 1)]
    [DataRow("Severity", "severity", 0)]
    [DataRow("abc", "abcdef", 3)]
    [DataRow("workspaceId", "workspaceId", 0)]
    public void BoundedDistance_CapsAtBoundPlusOne(string source, string target, int expected)
    {
        Assert.AreEqual(expected, UnknownArgumentDetector.BoundedDistance(source, target, bound: 2));
    }

    private static void AssertSingleUnknown(JsonElement payload, string name, string suggestion)
    {
        var entries = payload.GetProperty("_meta").GetProperty("unknownArguments");
        Assert.AreEqual(1, entries.GetArrayLength());
        Assert.AreEqual(name, entries[0].GetProperty("name").GetString());
        Assert.AreEqual(suggestion, entries[0].GetProperty("suggestion").GetString());
    }

    private static async Task<CallToolResult> CallAsync(
        InMemoryMcpClientServerHarness harness,
        string toolName,
        Dictionary<string, object?> arguments) =>
        await harness.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: CancellationToken.None);

    private static JsonElement ParseTextContent(CallToolResult result)
    {
        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync()
    {
        var services = new ServiceCollection();
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "unknown-argument-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "unknown-argument-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "unknown-argument-wire",
            cancellationToken: CancellationToken.None,
            serverOptions: options,
            serverServicesFactory: () => provider);
    }

    [McpServerToolType]
    private sealed class SyntheticTools
    {
        [McpServerTool(Name = ReadToolName)]
        public static string CompileCheck(string workspaceId, string? severity = null) =>
            JsonSerializer.Serialize(new { workspaceId, severity });

        [McpServerTool(Name = WriteToolName)]
        public static string WorkspaceClose(string workspaceId, bool drainProcesses = false)
        {
            if (workspaceId == "missing")
            {
                throw new KeyNotFoundException("Workspace 'missing' is not loaded.");
            }

            return JsonSerializer.Serialize(new { workspaceId, drainProcesses });
        }
    }
}
