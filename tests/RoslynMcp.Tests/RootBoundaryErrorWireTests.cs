using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

// The deprecated MCP Roots surface is intentionally isolated to this compatibility fixture, matching
// the equally isolated production adapter and McpRootsTestServerFactory.
#pragma warning disable MCP9005

namespace RoslynMcp.Tests;

/// <summary>
/// Real wire contract for <c>root-boundary-argument-refusals-public-message</c>: the production
/// <see cref="ClientRootPathValidator"/> runs behind the production <see cref="StructuredCallToolFilter"/>
/// over a real in-memory MCP client/server pair, and the assertions read the error envelope the
/// client actually receives — in both supported protocol eras (<c>2026-07-28</c> and
/// <c>2025-11-25</c>). Pins:
/// <list type="bullet">
///   <item>Caller boundary denials (path outside the configured roots, or outside a narrowing root)
///   stay <c>InvalidArgument</c> with the exact <see cref="ArgumentException"/> identity, the opaque
///   redaction message and no <c>schemaHint</c>.</item>
///   <item>An unusable request narrowing root is an <c>InvalidArgument</c> attributed to
///   <c>narrowingRootPaths</c> with the generic, path-free message.</item>
///   <item>Configured-root canonicalization faults and client-roots RPC failures are
///   <c>InvalidOperation</c> with a safe, path-free message — never <c>InvalidArgument</c>.</item>
///   <item>Cancellation of an in-flight roots request is not rewritten into a tool error.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class RootBoundaryErrorWireTests
{
    private const string _probeTool = "root_boundary_wire_probe";
    private const string _outsideRootMessage = "The requested path is outside the configured sanctioned-root boundary.";

    // A NUL byte makes Path.GetFullPath throw ArgumentException on every platform, which is the
    // canonicalization failure shape the boundary maps.
    private const string _unusableRoot = "unusable\0root";

    public static IEnumerable<object?[]> ProtocolEras()
    {
        yield return [null];
        yield return ["2025-11-25"];
    }

    [TestMethod]
    [DynamicData(nameof(ProtocolEras))]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task OutsideConfiguredRoot_IsInvalidArgument_WithOpaqueRefusalAndNoSchemaHint(string? protocolVersion)
    {
        var (root, outside) = CreateRoots();
        await using var harness = await CreateHarnessAsync(protocolVersion, [root]);

        var envelope = await CallProbeAsync(harness, new Dictionary<string, object?> { ["path"] = outside });

        AssertEnvelope(envelope, "InvalidArgument", nameof(ArgumentException), _outsideRootMessage);
        Assert.IsFalse(envelope.TryGetProperty("schemaHint", out _),
            "A boundary denial must not carry a schemaHint that would echo parameter metadata.");
        AssertNoPaths(envelope, root, outside);
    }

    [TestMethod]
    [DynamicData(nameof(ProtocolEras))]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task OutsideNarrowingRoot_IsInvalidArgument_WithOpaqueRefusal(string? protocolVersion)
    {
        var (root, _) = CreateRoots();
        var inside = Path.Combine(root, "file.cs");
        var narrowing = Path.Combine(TestTempRoot.Current, "rmcp-wire-narrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(narrowing);
        await using var harness = await CreateHarnessAsync(protocolVersion, [root]);

        var envelope = await CallProbeAsync(
            harness,
            new Dictionary<string, object?> { ["path"] = inside, ["narrowingRoot"] = narrowing });

        AssertEnvelope(envelope, "InvalidArgument", nameof(ArgumentException), _outsideRootMessage);
        Assert.IsFalse(envelope.TryGetProperty("schemaHint", out _));
        AssertNoPaths(envelope, root, narrowing);
    }

    [TestMethod]
    [DynamicData(nameof(ProtocolEras))]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task UnusableNarrowingRoot_IsInvalidArgument_AttributedToNarrowingParameter(string? protocolVersion)
    {
        var (root, _) = CreateRoots();
        await using var harness = await CreateHarnessAsync(protocolVersion, [root]);

        var envelope = await CallProbeAsync(
            harness,
            new Dictionary<string, object?> { ["path"] = Path.Combine(root, "file.cs"), ["narrowingRoot"] = _unusableRoot });

        AssertEnvelope(
            envelope,
            "InvalidArgument",
            nameof(ArgumentException),
            "Parameter 'narrowingRootPaths' is invalid. Check that all required parameters are provided and values match the expected types.");
        AssertNoPaths(envelope, root, "unusable");
    }

    [TestMethod]
    [DynamicData(nameof(ProtocolEras))]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task UnusableConfiguredRoot_IsInvalidOperation_WithSafePublicMessage(string? protocolVersion)
    {
        var (root, _) = CreateRoots();
        await using var harness = await CreateHarnessAsync(protocolVersion, [_unusableRoot]);

        var envelope = await CallProbeAsync(harness, new Dictionary<string, object?> { ["path"] = Path.Combine(root, "file.cs") });

        AssertEnvelope(
            envelope,
            "InvalidOperation",
            nameof(InvalidOperationException),
            "A configured sanctioned root could not be canonicalized. Check ROSLYNMCP_SANCTIONED_ROOTS on the server.");
        Assert.IsFalse(envelope.TryGetProperty("schemaHint", out _));
        AssertNoPaths(envelope, root, "unusable");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task FailedClientRootsRequest_IsInvalidOperation_WithSafePublicMessage()
    {
        var (root, _) = CreateRoots();
        await using var harness = await CreateHarnessAsync(
            "2025-11-25",
            [root],
            rootsHandler: (_, _) => throw new InvalidOperationException("client-private-detail:" + root));

        var envelope = await CallProbeAsync(harness, new Dictionary<string, object?> { ["path"] = Path.Combine(root, "file.cs") });

        AssertEnvelope(
            envelope,
            "InvalidOperation",
            nameof(InvalidOperationException),
            "Path validation failed because the client roots narrowing boundary could not be obtained.");
        AssertNoPaths(envelope, root, "client-private-detail");
    }

    [TestMethod]
    [DynamicData(nameof(ProtocolEras))]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ClientRootsNarrowing_ExcludedPath_IsInvalidArgument_WithOpaqueRefusal(string? protocolVersion)
    {
        var (root, _) = CreateRoots();
        var clientRoot = Path.Combine(TestTempRoot.Current, "rmcp-wire-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clientRoot);
        await using var harness = await CreateHarnessAsync(
            protocolVersion,
            [root],
            rootsHandler: (_, _) => ValueTask.FromResult(new ListRootsResult
            {
                Roots = [new Root { Uri = new Uri(clientRoot).AbsoluteUri }],
            }));

        var envelope = await CallProbeAsync(
            harness,
            new Dictionary<string, object?> { ["path"] = Path.Combine(root, "file.cs") });

        AssertEnvelope(envelope, "InvalidArgument", nameof(ArgumentException), _outsideRootMessage);
        Assert.IsFalse(envelope.TryGetProperty("schemaHint", out _));
        AssertNoPaths(envelope, root, clientRoot);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task CancelledInFlightClientRootsRequest_PropagatesCancellation_NotAnInvalidOperation()
    {
        var (root, _) = CreateRoots();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var harness = await CreateHarnessAsync(
            "2025-11-25",
            [root],
            rootsHandler: async (_, token) =>
            {
                requestStarted.TrySetResult();
                // Completes when the server's cancellation reaches this handler, which keeps the
                // harness disposable.
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new ListRootsResult { Roots = [] };
            });

        var validation = ClientRootPathValidator.ValidatePathAgainstRootsAsync(
            harness.Server,
            Path.Combine(root, "file.cs"),
            cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await cancellation.CancelAsync();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => validation);
        Assert.IsNotInstanceOfType<PublicInvalidOperationException>(error);
    }

    private static (string Root, string Outside) CreateRoots()
    {
        var parent = Path.Combine(TestTempRoot.Current, "rmcp-root-wire-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "sanctioned");
        var outside = Path.Combine(parent, "outside", "private.cs");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        return (root, outside);
    }

    private static async Task<JsonElement> CallProbeAsync(
        InMemoryMcpClientServerHarness harness,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await harness.Client.CallToolAsync(_probeTool, arguments, cancellationToken: CancellationToken.None);
        Assert.IsTrue(result.IsError is true, "The probe must fail with a tool error envelope.");
        var text = ((TextContentBlock)result.Content![0]).Text;
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static void AssertEnvelope(JsonElement envelope, string category, string exceptionType, string message)
    {
        Assert.AreEqual(category, envelope.GetProperty("category").GetString());
        Assert.AreEqual(exceptionType, envelope.GetProperty("exceptionType").GetString());
        Assert.AreEqual(message, envelope.GetProperty("message").GetString());
    }

    private static void AssertNoPaths(JsonElement envelope, params string[] forbidden)
    {
        var text = envelope.GetRawText();
        foreach (var fragment in forbidden)
        {
            Assert.IsFalse(
                text.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"The envelope must not echo '{fragment}': {text}");
        }
    }


    private static Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        IReadOnlyList<string> sanctionedRoots,
        Func<ListRootsRequestParams?, CancellationToken, ValueTask<ListRootsResult>>? rootsHandler = null) =>
        InMemoryMcpClientServerHarness.CreateAsync(
            new InMemoryMcpHarnessOptions(
                TransportName: "root-boundary-wire",
                ClientCapabilities: rootsHandler is null ? new ClientCapabilities() : new ClientCapabilities { Roots = new RootsCapability() },
                ClientHandlers: rootsHandler is null
                    ? new McpClientHandlers()
                    : new McpClientHandlers { RootsHandler = rootsHandler },
                DisposalFailureContext: "root-boundary-wire")
            {
                ProtocolVersion = protocolVersion,
                ServerHostFactory = (serverInput, serverOutput) =>
                {
                    var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                    hostBuilder.Logging.ClearProviders();
                    hostBuilder.Services.AddSingleton(new SecurityOptions { SanctionedRoots = sanctionedRoots });
                    hostBuilder.Services
                        .AddMcpServer()
                        .WithTools<RootBoundaryWireProbeTools>()
                        .WithRequestFilters(static filters =>
                            filters.AddCallToolFilter(StructuredCallToolFilter.Create))
                        .WithStreamServerTransport(serverInput, serverOutput);
                    return hostBuilder.Build();
                },
            },
            CancellationToken.None);


    [McpServerToolType]
    private sealed class RootBoundaryWireProbeTools
    {
        [McpServerTool(Name = _probeTool)]
        public static async Task<string> ProbeAsync(
            McpServer server,
            string path,
            string? narrowingRoot = null,
            CancellationToken cancellationToken = default) =>
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server,
                path,
                cancellationToken,
                narrowingRootPaths: narrowingRoot is null ? null : [narrowingRoot]).ConfigureAwait(false);
    }
}
