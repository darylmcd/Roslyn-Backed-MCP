using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceResourceListNotificationWireTests
{
    private const string _legacyProtocolVersion = "2025-11-25";

    [TestMethod]
    public async Task StaticResourceList_RemainsByteEquivalentWithoutChangeNotifications()
    {
        MsBuildInitializer.EnsureInitialized();
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var sampleSolution = TestFixtureFileSystem.FindFixturePath(
            repositoryRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");

        foreach (var protocolVersion in new string?[] { _legacyProtocolVersion, null })
        {
            await using var harness = await CreateHarnessAsync(
                repositoryRoot,
                protocolVersion,
                CancellationToken.None);
            var expectedResources = await SnapshotResourcesAsync(harness);

            var load = await harness.Client.CallToolAsync(
                "workspace_load",
                Args(("path", sampleSolution), ("prewarm", false), ("autoRestore", false)),
                cancellationToken: CancellationToken.None);
            Assert.IsFalse(load.IsError is true, load.TextPayload());
            using var loadPayload = JsonDocument.Parse(load.TextPayload());
            var workspaceId = loadPayload.RootElement.GetProperty("workspaceId").GetString()!;
            Assert.AreEqual(expectedResources, await SnapshotResourcesAsync(harness));

            var reload = await harness.Client.CallToolAsync(
                "workspace_reload",
                Args(("workspaceId", workspaceId), ("autoRestore", false)),
                cancellationToken: CancellationToken.None);
            Assert.IsFalse(reload.IsError is true, reload.TextPayload());
            Assert.AreEqual(expectedResources, await SnapshotResourcesAsync(harness));

            var close = await harness.Client.CallToolAsync(
                "workspace_close",
                Args(("workspaceId", workspaceId), ("drainProcesses", false)),
                cancellationToken: CancellationToken.None);
            Assert.IsFalse(close.IsError is true, close.TextPayload());
            Assert.AreEqual(expectedResources, await SnapshotResourcesAsync(harness));

            foreach (var rawMessage in harness.RawServerMessages)
            {
                using var document = JsonDocument.Parse(rawMessage);
                Assert.IsFalse(
                    document.RootElement.TryGetProperty("method", out var method) &&
                    method.GetString() == NotificationMethods.ResourceListChangedNotification,
                    $"Static workspace lifecycle emitted a false resource-list notification: {rawMessage}");
            }
        }
    }

    private static async Task<string> SnapshotResourcesAsync(
        InMemoryMcpClientServerHarness harness)
    {
        var resources = await harness.Client.ListResourcesAsync(
            new ListResourcesRequestParams(),
            CancellationToken.None);
        return JsonSerializer.Serialize(resources.Resources);
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
        services.AddSingleton<IServerObservabilitySink, DisabledServerObservabilitySink>();
        services.AddSingleton<ServerObservabilityReporter>();
        services
            .AddMcpServer(static options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "roslyn-mcp-resource-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            .WithMessageFilters(static filters =>
                filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(static filters =>
            {
                filters.AddListToolsFilter(StaticListResultFilter.CreateTools);
                filters.AddListResourcesFilter(StaticListResultFilter.CreateResources);
                filters.AddListResourceTemplatesFilter(StaticListResultFilter.CreateResourceTemplates);
                filters.AddCallToolFilter(StructuredCallToolFilter.Create);
            });
        services.AddRoslynMcpSurfaceRegistrationPolicy(ToolTierSelection.All);

        var provider = services.BuildServiceProvider();
        var serverOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"workspace-resource-wire-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "workspace-resource-list-notification-wire",
            cancellationToken: cancellationToken,
            protocolVersion: protocolVersion,
            serverOptions: serverOptions,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static Dictionary<string, object?> Args(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);
}
