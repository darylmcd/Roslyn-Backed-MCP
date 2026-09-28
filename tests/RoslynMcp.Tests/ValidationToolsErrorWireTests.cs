using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// validation-tools-error-envelope-not-iserror: the validation tools used to catch every
/// exception and return the error envelope as a SUCCESSFUL tool result, so the wire frame
/// carried <c>isError: false</c> and no <c>_meta</c>. These tests drive the real
/// <see cref="StructuredCallToolFilter"/> over an in-memory MCP transport and assert the
/// failure now reaches the client as <c>isError: true</c> while keeping the category and the
/// validation tools' any-category <c>schemaHint</c>.
/// </summary>
[TestClass]
public sealed class ValidationToolsErrorWireTests : SharedWorkspaceTestBase
{
    private static string _workspaceId = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await LoadSharedSampleWorkspaceAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task BuildProject_UnknownProject_ReturnsWireIsErrorWithCategoryAndSchemaHint()
    {
        // The unknown project is rejected by project resolution before any `dotnet build` runs.
        var tool = McpServerTool.Create(
            (string workspaceId, string projectName, CancellationToken ct = default) =>
                ValidationTools.BuildProject(WorkspaceExecutionGate, BuildService, workspaceId, projectName, ct),
            new McpServerToolCreateOptions { Name = "build_project" });
        await using var harness = await CreateHarnessAsync(tool);

        var envelope = await CallAndReadErrorEnvelopeAsync(
            harness,
            "build_project",
            new Dictionary<string, object?> { ["workspaceId"] = _workspaceId, ["projectName"] = "Does.Not.Exist" });

        Assert.AreEqual("InvalidOperation", envelope["category"]?.GetValue<string>(), envelope.ToJsonString());
        Assert.AreEqual("build_project", envelope["tool"]?.GetValue<string>());
        StringAssert.Contains(envelope["schemaHint"]?.GetValue<string>(), "build_project(");
        StringAssert.Contains(envelope["schemaHint"]?.GetValue<string>(), "projectName");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TestRelated_PartialSourceLocation_ReturnsWireIsErrorWithCategoryAndSchemaHint()
    {
        var tool = McpServerTool.Create(
            (string workspaceId, string? filePath = null, int? line = null, int? column = null, CancellationToken ct = default) =>
                ValidationTools.FindRelatedTests(
                    WorkspaceExecutionGate, TestDiscoveryService, workspaceId, filePath, line, column, ct: ct),
            new McpServerToolCreateOptions { Name = "test_related" });
        await using var harness = await CreateHarnessAsync(tool);

        var envelope = await CallAndReadErrorEnvelopeAsync(
            harness,
            "test_related",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = _workspaceId,
                ["filePath"] = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "Program.cs"),
                ["line"] = 1,
            });

        Assert.AreEqual("InvalidArgument", envelope["category"]?.GetValue<string>(), envelope.ToJsonString());
        Assert.AreEqual("test_related", envelope["tool"]?.GetValue<string>());
        StringAssert.Contains(envelope["message"]?.GetValue<string>(), "column");
        StringAssert.Contains(envelope["schemaHint"]?.GetValue<string>(), "test_related(");
    }

    private static async Task<JsonObject> CallAndReadErrorEnvelopeAsync(
        InMemoryMcpClientServerHarness harness,
        string toolName,
        Dictionary<string, object?> arguments)
    {
        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(toolName, arguments);
        Assert.IsTrue(result.IsError, $"{toolName} failure must be reported as isError=true.");

        var frame = harness.RawServerMessages.Skip(prior)
            .Select(raw => JsonNode.Parse(raw))
            .OfType<JsonObject>()
            .Single(message => message["result"] is not null || message["error"] is not null);
        Assert.IsNull(frame["error"]);
        var wireResult = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        Assert.AreEqual(true, wireResult["isError"]?.GetValue<bool>());
        var envelope = Assert.IsInstanceOfType<JsonObject>(
            JsonNode.Parse(wireResult["content"]![0]!["text"]!.GetValue<string>()));
        Assert.AreEqual(true, envelope["error"]?.GetValue<bool>());
        Assert.IsNotNull(envelope["_meta"], $"The shared filter must inject _meta. Envelope: {envelope.ToJsonString()}");
        return envelope;
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(McpServerTool tool)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceManager>(WorkspaceManager);
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "validation-tools-error-wire", Version = "1.0.0" };
                options.ToolCollection = new McpServerPrimitiveCollection<McpServerTool> { tool };
            })
            .WithMessageFilters(filters => filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(filters => filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "validation-tools-error-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "validation-tools-error-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
            serverServicesFactory: () => provider,
            serverOptions: provider.GetRequiredService<IOptions<McpServerOptions>>().Value,
            captureServerMessages: true);
    }
}
