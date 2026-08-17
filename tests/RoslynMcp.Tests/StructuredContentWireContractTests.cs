using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Raw-wire guard for the eight tools that advertise an output schema. The regression became
/// observable when the SDK 1.4.1-to-2.1.0 upgrade began deriving structured content from CLR
/// return values and schema registration subsequently activated those schemas. A pre-serialized
/// <see cref="string"/> therefore became a JSON string instead of the advertised object.
/// </summary>
[TestClass]
public sealed class StructuredContentWireContractTests
{
    private const string _legacyProtocolVersion = "2025-11-25";
    private const string _modernProtocolVersion = "2026-07-28";

    [TestMethod]
    public async Task SchemaTools_RawWirePayloadsMatchAdvertisedSchemasAcrossProtocolEras()
    {
        MsBuildInitializer.EnsureInitialized();
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var sampleSolution = TestFixtureFileSystem.FindFixturePath(
            repositoryRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
        var sampleProject = Path.Combine(
            Path.GetDirectoryName(sampleSolution)!,
            "SampleLib",
            "SampleLib.csproj");

        var protocols = new (string? Requested, string Expected, bool Modern)[]
        {
            (_legacyProtocolVersion, _legacyProtocolVersion, false),
            (null, _modernProtocolVersion, true),
        };

        foreach (var protocol in protocols)
        {
            await using var harness = await CreateHarnessAsync(
                repositoryRoot,
                protocol.Requested,
                CancellationToken.None);
            Assert.AreEqual(protocol.Expected, harness.Client.NegotiatedProtocolVersion);

            var listedTools = await harness.Client.ListToolsAsync(
                new ListToolsRequestParams(),
                CancellationToken.None);
            var advertisedSchemas = listedTools.Tools
                .Where(static tool => tool.OutputSchema is not null)
                .ToDictionary(
                    static tool => tool.Name,
                    static tool => JsonNode.Parse(tool.OutputSchema!.Value.GetRawText())!,
                    StringComparer.Ordinal);

            // Exercise no-workspace and empty-list success branches before loading a target.
            var preLoadCases = new ToolCase[]
            {
                new("server info", "server_info"),
                new("server heartbeat", "server_heartbeat"),
                new("workspace list empty summary", "workspace_list", Args(("verbose", false))),
                new("workspace list empty verbose", "workspace_list", Args(("verbose", true))),
            };
            foreach (var toolCase in preLoadCases)
            {
                await AssertToolCaseAsync(
                    harness,
                    advertisedSchemas,
                    toolCase,
                    protocol.Modern);
            }

            var workspaceId = await LoadWorkspaceAsync(harness, sampleSolution);
            var loadedCases = new ToolCase[]
            {
                new("workspace list single summary", "workspace_list", Args(("verbose", false))),
                new("workspace list single verbose", "workspace_list", Args(("verbose", true))),
                new("workspace status summary", "workspace_status", Args(("workspaceId", workspaceId), ("verbose", false))),
                new("workspace status verbose", "workspace_status", Args(("workspaceId", workspaceId), ("verbose", true))),
                new("workspace health", "workspace_health", Args(("workspaceId", workspaceId))),
                new("workspace readiness", "workspace_readiness_report", Args(("workspaceId", workspaceId))),
                new("workspace support bundle", "workspace_support_bundle", Args(("workspaceId", workspaceId))),
                new("workspace drift", "workspace_drift_check", Args(("workspaceId", workspaceId))),
                new("readiness missing-workspace success", "workspace_readiness_report", Args(("workspaceId", "missing-wire-workspace"))),
                new("support missing-workspace success", "workspace_support_bundle", Args(("workspaceId", "missing-wire-workspace"))),
            };
            foreach (var toolCase in loadedCases)
            {
                await AssertToolCaseAsync(
                    harness,
                    advertisedSchemas,
                    toolCase,
                    protocol.Modern);
            }

            _ = await LoadWorkspaceAsync(harness, sampleProject);
            await AssertToolCaseAsync(
                harness,
                advertisedSchemas,
                new ToolCase("workspace list multiple summary", "workspace_list", Args(("verbose", false))),
                protocol.Modern);
            await AssertToolCaseAsync(
                harness,
                advertisedSchemas,
                new ToolCase("workspace list multiple verbose", "workspace_list", Args(("verbose", true))),
                protocol.Modern);
        }
    }

    [TestMethod]
    public async Task ToolWithoutOutputSchema_StillEmitsNoStructuredContent()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        await using var harness = await CreateHarnessAsync(
            repositoryRoot,
            protocolVersion: null,
            CancellationToken.None);

        var result = await harness.Client.CallToolAsync(
            "recommend_workflow",
            cancellationToken: CancellationToken.None);

        Assert.IsNull(
            result.StructuredContent,
            "A tool without outputSchema must keep structuredContent absent.");
    }

    private static async Task AssertToolCaseAsync(
        InMemoryMcpClientServerHarness harness,
        IReadOnlyDictionary<string, JsonNode> advertisedSchemas,
        ToolCase toolCase,
        bool modernProtocol)
    {
        Assert.IsTrue(
            advertisedSchemas.TryGetValue(toolCase.ToolName, out var schema),
            $"{toolCase.ToolName} must advertise an output schema.");

        var priorMessageCount = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(
            toolCase.ToolName,
            toolCase.Arguments,
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(
            result.IsError is true,
            $"{toolCase.Label} failed: {DescribeText(result)}");

        var rawResult = FindSingleNewResult(
            harness.RawServerMessages,
            priorMessageCount,
            toolCase.Label);
        if (modernProtocol)
        {
            Assert.AreEqual(
                "complete",
                rawResult.GetProperty("resultType").GetString(),
                $"{toolCase.Label} must carry the modern result discriminator.");
        }
        else
        {
            Assert.IsFalse(
                rawResult.TryGetProperty("resultType", out _),
                $"{toolCase.Label} leaked resultType to a legacy client.");
        }

        var rawStructured = rawResult.GetProperty("structuredContent");
        Assert.AreEqual(
            JsonValueKind.Object,
            rawStructured.ValueKind,
            $"{toolCase.Label} must emit an object, not a serialized JSON string.");
        Assert.IsTrue(
            GeneratedJsonSchemaMatcher.Matches(rawStructured, schema!),
            $"{toolCase.Label} does not satisfy its session-advertised output schema: " +
            rawStructured.GetRawText());

        var text = rawResult.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.IsNotNull(text, $"{toolCase.Label} must retain the legacy text payload.");
        var textNode = JsonNode.Parse(text) as JsonObject;
        Assert.IsNotNull(textNode, $"{toolCase.Label} text payload must remain object-rooted JSON.");
        Assert.IsTrue(
            textNode.Remove("_meta"),
            $"{toolCase.Label} text payload must carry the observability _meta block.");
        Assert.IsTrue(
            JsonNode.DeepEquals(textNode, JsonNode.Parse(rawStructured.GetRawText())),
            $"{toolCase.Label} structuredContent must deep-equal text after removing _meta.");

        Assert.IsNotNull(result.StructuredContent);
        Assert.IsTrue(
            JsonNode.DeepEquals(
                JsonNode.Parse(result.StructuredContent.Value.GetRawText()),
                JsonNode.Parse(rawStructured.GetRawText())),
            $"{toolCase.Label} client projection diverged from the raw server frame.");
    }

    private static async Task<string> LoadWorkspaceAsync(
        InMemoryMcpClientServerHarness harness,
        string path)
    {
        var result = await harness.Client.CallToolAsync(
            "workspace_load",
            Args(("path", path), ("prewarm", false), ("autoRestore", false)),
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(result.IsError is true, $"workspace_load failed: {DescribeText(result)}");

        using var document = JsonDocument.Parse(result.TextPayload());
        return document.RootElement.GetProperty("workspaceId").GetString()
            ?? throw new AssertFailedException("workspace_load omitted workspaceId.");
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string repositoryRoot,
        string? protocolVersion,
        CancellationToken cancellationToken)
    {
        var hostAssembly = typeof(HostAssemblyMarker).Assembly;
        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions { SanctionedRoots = [repositoryRoot] },
            new ScriptingServiceOptions());
        services
            .AddMcpServer(static options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "roslyn-mcp-test",
                    Version = "1.0.0",
                };
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            .WithRequestFilters(static filters =>
            {
                filters.AddListToolsFilter(StaticListResultFilter.CreateTools);
                filters.AddCallToolFilter(StructuredCallToolFilter.Create);
            });
        services.AddRoslynMcpSurfaceRegistrationPolicy(ToolTierSelection.All);

        var provider = services.BuildServiceProvider();
        var serverOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"structured-content-wire-{protocolVersion ?? _modernProtocolVersion}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "structured-content-wire",
            cancellationToken: cancellationToken,
            protocolVersion: protocolVersion,
            serverOptions: serverOptions,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static Dictionary<string, object?> Args(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static JsonElement FindSingleNewResult(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount,
        string caseName)
    {
        var results = new List<JsonElement>();
        foreach (var rawMessage in rawMessages.Skip(priorMessageCount))
        {
            using var document = JsonDocument.Parse(rawMessage);
            if (document.RootElement.TryGetProperty("result", out var result))
            {
                results.Add(result.Clone());
            }
        }

        Assert.HasCount(1, results, $"Expected one raw response result for {caseName}.");
        return results[0];
    }

    private static string DescribeText(CallToolResult result) =>
        result.Content is [TextContentBlock text, ..] ? text.Text : "<no text content>";

    private sealed record ToolCase(
        string Label,
        string ToolName,
        IReadOnlyDictionary<string, object?>? Arguments = null);
}
