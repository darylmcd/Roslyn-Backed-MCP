using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// test-coverage-unexpected-error-not-iserror: <c>test_coverage</c> and its
/// <c>get_test_coverage_map</c> alias used to catch every exception and return a
/// success-shaped <c>failureEnvelope</c> result, so the wire frame carried
/// <c>isError: false</c>. These tests drive the real <see cref="StructuredCallToolFilter"/> over an
/// in-memory MCP transport with a runner that throws, and assert the failure reaches the client
/// as <c>isError: true</c> with the shared, secret-safe <c>InternalError</c> envelope.
/// </summary>
[TestClass]
public sealed class TestCoverageErrorWireTests
{
    private const string Sentinel = "SECRET-SENTINEL-C:/private/coverage.runsettings";

    [TestMethod]
    [DataRow("test_coverage")]
    [DataRow("get_test_coverage_map")]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task UnexpectedRunnerFailure_ReturnsWireIsErrorWithInternalErrorEnvelope(string toolName)
    {
        var gate = new PassThroughWorkspaceExecutionGate();
        var workspace = new FailClosedWorkspaceManagerStub
        {
            GetStatusAsyncHandler = BuildEmptyStatus,
        };
        var executor = new GatedCommandExecutor(
            workspace, new ThrowingCommandRunner(), NullLogger<GatedCommandExecutor>.Instance);
        var options = new ValidationServiceOptions();

        // Delegate shapes mirror the production tools' (workspaceId, projectName, ct) wire
        // parameters; the Core entry points are called directly so the real filter is the only
        // layer between the throw and the wire.
        var tool = McpServerTool.Create(
            (string workspaceId, string? projectName = null, CancellationToken ct = default) =>
                string.Equals(toolName, "test_coverage", StringComparison.Ordinal)
                    ? TestCoverageTools.RunTestCoverage(gate, workspace, executor, options, workspaceId, projectName, ct: ct)
                    : TestCoverageTools.GetTestCoverageMap(gate, workspace, executor, options, workspaceId, projectName, ct: ct),
            new McpServerToolCreateOptions { Name = toolName });
        await using var harness = await CreateHarnessAsync(tool);

        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(
            toolName,
            new Dictionary<string, object?> { ["workspaceId"] = "ws-coverage-wire" });
        Assert.IsTrue(result.IsError, $"{toolName} unexpected failure must be reported as isError=true.");

        var frame = harness.RawServerMessages.Skip(prior)
            .Select(raw => JsonNode.Parse(raw))
            .OfType<JsonObject>()
            .Single(message => message["result"] is not null || message["error"] is not null);
        Assert.IsNull(frame["error"]);
        var wireResult = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        Assert.AreEqual(true, wireResult["isError"]?.GetValue<bool>());

        var text = wireResult["content"]![0]!["text"]!.GetValue<string>();
        Assert.IsFalse(text.Contains(Sentinel, StringComparison.Ordinal),
            "The raw exception message must not reach the client.");
        var envelope = Assert.IsInstanceOfType<JsonObject>(JsonNode.Parse(text));
        Assert.AreEqual(true, envelope["error"]?.GetValue<bool>());
        Assert.AreEqual("InternalError", envelope["category"]?.GetValue<string>(), envelope.ToJsonString());
        Assert.AreEqual(toolName, envelope["tool"]?.GetValue<string>());
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope["correlationId"]?.GetValue<string>()),
            "InternalError envelopes must carry a correlation id.");
        Assert.IsNotNull(envelope["_meta"], $"The shared filter must inject _meta. Envelope: {envelope.ToJsonString()}");
    }

    // An empty project list makes the partition empty, so execution reaches the classic
    // `dotnet test` path and therefore the throwing runner.
    private static WorkspaceStatusDto BuildEmptyStatus(string workspaceId) =>
        new(
            WorkspaceId: workspaceId,
            LoadedPath: "C:\\fake\\workspace\\Sample.slnx",
            WorkspaceVersion: 1,
            SnapshotToken: "snapshot",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 0,
            DocumentCount: 0,
            Projects: [],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: []);

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(McpServerTool tool)
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "test-coverage-error-wire", Version = "1.0.0" };
                options.ToolCollection = new McpServerPrimitiveCollection<McpServerTool> { tool };
            })
            .WithMessageFilters(filters => filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(filters => filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "test-coverage-error-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "test-coverage-error-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
            serverServicesFactory: () => provider,
            serverOptions: provider.GetRequiredService<IOptions<McpServerOptions>>().Value,
            captureServerMessages: true);
    }

    /// <summary>Not an InvalidOperation/Argument/IO type, so the filter's terminal InternalError branch handles it.</summary>
    private sealed class UnclassifiedRunnerException(string message, Exception inner) : Exception(message, inner);

    private sealed class ThrowingCommandRunner : IDotnetCommandRunner
    {
        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            throw new UnclassifiedRunnerException(Sentinel, new IOException(Sentinel));
    }
}
