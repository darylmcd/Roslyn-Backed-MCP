using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServerDiscoveryWireTests
{
    [TestMethod]
    public async Task InitializeAndToolsList_ProjectInstructionsSchemasOrderingAndCachingHints()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        var hostAssembly = typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly;
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "roslyn-mcp-test", Version = "1.0.0" };
                options.ServerInstructions = ServerInstructions.Text;
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            .WithRequestFilters(filters => filters.AddListToolsFilter(StaticListResultFilter.CreateTools));
        builder.Services.AddRoslynMcpSurfaceRegistrationPolicy(ToolTierSelection.All);
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        if (options.ToolCollection is null)
        {
            Assert.Fail("MCP tool collection was not initialized.");
        }

        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "server-discovery-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "server-discovery-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
            serverOptions: options);

        Assert.AreEqual(
            "2026-07-28",
            harness.Client.NegotiatedProtocolVersion);
        var instructions = harness.Client.ServerInstructions;
        Assert.AreEqual(ServerInstructions.Text, instructions);
        Assert.IsNotNull(instructions);
        Assert.IsTrue(instructions.Length <= ServerInstructions.ClientCharacterLimit);

        var result = await harness.Client.ListToolsAsync(
            new ListToolsRequestParams(),
            CancellationToken.None);
        var names = result.Tools.Select(static tool => tool.Name).ToArray();
        var sorted = names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var withSchemas = result.Tools
            .Where(static tool => tool.OutputSchema is not null)
            .Select(static tool => tool.Name)
            .ToArray();

        CollectionAssert.AreEqual(sorted, names);
        CollectionAssert.AreEquivalent(ToolOutputSchemaIndex.RegisteredToolNames.ToArray(), withSchemas);
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, result.TimeToLive);
        Assert.AreEqual(CacheScope.Private, result.CacheScope);
    }

    [TestMethod]
    public async Task StableOnlyProfile_FiltersDiscoveryCatalogAndDirectDispatchAcrossEverySurface()
    {
        var selection = ToolTierSelection.Parse("stable");
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        var hostAssembly = typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly;
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "roslyn-mcp-test", Version = "1.0.0" };
                options.ServerInstructions = ServerInstructions.For(selection);
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(StaticListResultFilter.CreateTools);
                filters.AddListPromptsFilter(StaticListResultFilter.CreatePrompts);
                filters.AddListResourcesFilter(StaticListResultFilter.CreateResources);
                filters.AddListResourceTemplatesFilter(StaticListResultFilter.CreateResourceTemplates);
                filters.AddReadResourceFilter(ResourceReadResultFilter.Create);
            });
        builder.Services.AddRoslynMcpSurfaceRegistrationPolicy(selection);
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "stable-server-discovery-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "stable-server-discovery-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
            serverOptions: options,
            serverServicesFactory: () => new ServiceCollection()
                .AddSingleton(selection)
                .BuildServiceProvider());

        var tools = await harness.Client.ListToolsAsync(
            new ListToolsRequestParams(),
            CancellationToken.None);
        var expectedToolNames = SelectedNames(ServerSurfaceCatalog.Tools);
        CollectionAssert.AreEquivalent(expectedToolNames, tools.Tools.Select(static tool => tool.Name).ToArray());

        var prompts = await harness.Client.ListPromptsAsync(
            new ListPromptsRequestParams(),
            CancellationToken.None);
        Assert.IsEmpty(prompts.Prompts);
        AssertPrivateCacheHints(prompts.TimeToLive, prompts.CacheScope);

        var resources = await harness.Client.ListResourcesAsync(
            new ListResourcesRequestParams(),
            CancellationToken.None);
        var expectedResourceNames = ServerSurfaceCatalog.Resources
            .Where(static entry => entry.SupportTier == "stable" && !entry.UriTemplate!.Contains('{', StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(expectedResourceNames, resources.Resources.Select(static resource => resource.Name).ToArray());
        AssertPrivateCacheHints(resources.TimeToLive, resources.CacheScope);
        var catalogListing = resources.Resources.Single(static resource => resource.Name == "server_catalog");
        StringAssert.Contains(catalogListing.Description, "active support-tier profile");
        Assert.IsFalse(catalogListing.Description.Contains("catalog/full", StringComparison.Ordinal));

        var resourceTemplates = await harness.Client.ListResourceTemplatesAsync(
            new ListResourceTemplatesRequestParams(),
            CancellationToken.None);
        var expectedTemplateNames = ServerSurfaceCatalog.Resources
            .Where(static entry => entry.SupportTier == "stable" && entry.UriTemplate!.Contains('{', StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(
            expectedTemplateNames,
            resourceTemplates.ResourceTemplates.Select(static resource => resource.Name).ToArray());
        AssertPrivateCacheHints(resourceTemplates.TimeToLive, resourceTemplates.CacheScope);

        var catalogResult = await harness.Client.ReadResourceAsync(
            new ReadResourceRequestParams { Uri = "roslyn://server/catalog" },
            CancellationToken.None);
        Assert.HasCount(1, catalogResult.Contents);
        AssertResourceReadCacheHints(catalogResult);
        var catalogContent = Assert.IsInstanceOfType<TextResourceContents>(catalogResult.Contents[0]);
        using (var catalog = JsonDocument.Parse(catalogContent.Text))
        {
            var root = catalog.RootElement;
            Assert.AreEqual(expectedToolNames.Length, root.GetProperty("toolCount").GetInt32());
            Assert.AreEqual(0, root.GetProperty("promptCount").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("toolsResourceTemplate").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("promptsResourceTemplate").ValueKind);
            Assert.IsTrue(root.GetProperty("resources").EnumerateArray().All(
                static entry => entry.GetProperty("supportTier").GetString() == "stable"));
            var advertisedToolNames = expectedToolNames.ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(root.GetProperty("workflowHints").EnumerateArray().All(hint =>
                hint.GetProperty("toolSequence").EnumerateArray().All(tool =>
                    advertisedToolNames.Contains(tool.GetString()!))));
        }

        var templatesResult = await harness.Client.ReadResourceAsync(
            new ReadResourceRequestParams { Uri = "roslyn://server/resource-templates" },
            CancellationToken.None);
        Assert.HasCount(1, templatesResult.Contents);
        AssertResourceReadCacheHints(templatesResult);
        var templatesContent = Assert.IsInstanceOfType<TextResourceContents>(templatesResult.Contents[0]);
        using (var templates = JsonDocument.Parse(templatesContent.Text))
        {
            Assert.IsTrue(templates.RootElement.GetProperty("resources").EnumerateArray().All(
                static entry => entry.GetProperty("supportTier").GetString() == "stable"));
        }

        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.CallToolAsync(
            "recommend_workflow",
            cancellationToken: CancellationToken.None));
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.GetPromptAsync(
            "explain_error",
            cancellationToken: CancellationToken.None));
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.ReadResourceAsync(
            "roslyn://server/catalog/full",
            cancellationToken: CancellationToken.None));

        static string[] SelectedNames(IReadOnlyList<SurfaceEntry> entries) => entries
            .Where(static entry => entry.SupportTier == "stable")
            .Select(static entry => entry.Name)
            .ToArray();

        static void AssertPrivateCacheHints(TimeSpan? timeToLive, CacheScope? cacheScope)
        {
            Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, timeToLive);
            Assert.AreEqual(CacheScope.Private, cacheScope);
        }

        static void AssertResourceReadCacheHints(ReadResourceResult result)
        {
            Assert.AreEqual(ResourceReadResultFilter.CacheTimeToLive, result.TimeToLive);
            Assert.AreEqual(CacheScope.Private, result.CacheScope);
        }
    }
}
