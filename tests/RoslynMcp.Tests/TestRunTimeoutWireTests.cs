using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestRunTimeoutWireTests : SharedWorkspaceTestBase
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
    [DataRow("2025-11-25")]
    [DataRow(null)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task UnfilteredGateTimeout_HasStructuredWireError_AndFilteredRetrySucceeds(string? protocol)
    {
        var clock = new FakeTimeProvider();
        using var gate = CreateGate(clock);
        var runner = new ControlledRunner(() => clock.Advance(TimeSpan.FromMinutes(2)));
        await using var harness = await CreateHarnessAsync(protocol, gate, runner);
        Assert.AreEqual(protocol ?? "2026-07-28", harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync("test_run",
            new Dictionary<string, object?> { ["workspaceId"] = _workspaceId });
        Assert.IsTrue(result.IsError);
        Assert.AreEqual(1, runner.Calls);

        var frame = harness.RawServerMessages.Skip(prior)
            .Select(raw => JsonNode.Parse(raw))
            .OfType<JsonObject>()
            .Single(message => message["result"] is not null || message["error"] is not null);
        Assert.IsNull(frame["error"]);
        var wireResult = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        Assert.AreEqual(true, wireResult["isError"]?.GetValue<bool>());
        var envelope = Assert.IsInstanceOfType<JsonObject>(
            JsonNode.Parse(wireResult["content"]![0]!["text"]!.GetValue<string>()));
        Assert.AreEqual("Timeout", envelope["category"]?.GetValue<string>());
        Assert.AreEqual("test_run", envelope["tool"]?.GetValue<string>());
        Assert.IsNotNull(envelope["_meta"]);
        StringAssert.Contains(envelope["message"]?.GetValue<string>(), "ROSLYNMCP_REQUEST_TIMEOUT_SECONDS");
        Assert.AreEqual(protocol is null ? "complete" : null, wireResult["resultType"]?.GetValue<string>());

        var retry = await harness.Client.CallToolAsync("test_run",
            new Dictionary<string, object?> { ["workspaceId"] = _workspaceId, ["filter"] = "ClassName=Focused" });
        Assert.IsFalse(retry.IsError is true);
        Assert.AreEqual(2, runner.Calls);
        using var success = JsonDocument.Parse(((TextContentBlock)retry.Content[0]).Text);
        Assert.AreEqual(1, success.RootElement.GetProperty("passed").GetInt32());
    }

    [TestMethod]
    public async Task CallerCancellationDuringRunner_PropagatesWithItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var gate = CreateGate(new FakeTimeProvider());
        var runner = new ControlledRunner(cancellation.Cancel);
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ValidationTools.RunTests(gate, runner, _workspaceId, ct: cancellation.Token));
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(exception.CancellationToken.IsCancellationRequested);
        Assert.AreEqual(1, runner.Calls);
    }

    private static WorkspaceExecutionGate CreateGate(FakeTimeProvider clock) => new(
        new ExecutionGateOptions { RequestTimeout = TimeSpan.FromMinutes(2), OnStale = StalenessPolicy.Off },
        WorkspaceManager, clock);

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocol, IWorkspaceExecutionGate gate, ITestRunnerService runner)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceManager>(WorkspaceManager);
        var tool = McpServerTool.Create(
            (string workspaceId, string? filter = null, CancellationToken ct = default) =>
                ValidationTools.RunTests(gate, runner, workspaceId, filter: filter, ct: ct),
            new McpServerToolCreateOptions { Name = "test_run" });
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "test-run-timeout-wire", Version = "1.0.0" };
                options.ToolCollection = new McpServerPrimitiveCollection<McpServerTool> { tool };
            })
            .WithMessageFilters(filters => filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(filters => filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "test-run-timeout-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "test-run-timeout-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocol,
            serverServicesFactory: () => provider,
            serverOptions: provider.GetRequiredService<IOptions<McpServerOptions>>().Value,
            captureServerMessages: true);
    }

    private sealed class ControlledRunner(Action cancelUnfiltered) : ITestRunnerService
    {
        public int Calls { get; private set; }

        public Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            Calls++;
            if (filter is null)
            {
                cancelUnfiltered();
                ct.ThrowIfCancellationRequested();
                Assert.Fail("The controlled cancellation must fire before the runner completes.");
            }

            return Task.FromResult(new TestRunResultDto(
                new CommandExecutionDto("dotnet", [], string.Empty, string.Empty, 0, true, 1, string.Empty, string.Empty),
                1, 1, 0, 0, []));
        }
    }
}
