using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LspSourceLocationAliasWireTests
{
    private const string ToolName = "symbol_info";

    [TestMethod]
    public async Task SourceLocationAliases_TraverseFilterAndSdkBinding()
    {
        await using var harness = await CreateHarnessAsync();

        var characterOnly = await CallAsync(harness, new()
        {
            ["filePath"] = "sample.cs",
            ["line"] = 4,
            ["character"] = 6,
        });
        AssertBoundLocation(characterOnly, expectedColumn: 7);

        var columnOnly = await CallAsync(harness, new()
        {
            ["filePath"] = "sample.cs",
            ["line"] = 4,
            ["column"] = 7,
        });
        AssertBoundLocation(columnOnly, expectedColumn: 7);

        var agreeingAliases = await CallAsync(harness, new()
        {
            ["filePath"] = "sample.cs",
            ["line"] = 4,
            ["column"] = 7,
            ["character"] = 6,
        });
        AssertBoundLocation(agreeingAliases, expectedColumn: 7);

        var conflict = await CallAsync(harness, new()
        {
            ["filePath"] = "sample.cs",
            ["line"] = 4,
            ["column"] = 8,
            ["character"] = 6,
        });
        Assert.IsTrue(conflict.IsError is true);
        var error = ParseTextContent(conflict);
        Assert.AreEqual(ToolErrorHandler.ErrorCategories.InvalidArgument.ToString(),
            error.GetProperty("category").GetString());
        StringAssert.Contains(
            error.GetProperty("message").GetString(),
            "column must equal character + 1 (7)");
    }

    private static async Task<CallToolResult> CallAsync(
        InMemoryMcpClientServerHarness harness,
        Dictionary<string, object?> arguments) =>
        await harness.Client.CallToolAsync(
            ToolName,
            arguments,
            cancellationToken: CancellationToken.None);

    private static void AssertBoundLocation(CallToolResult result, int expectedColumn)
    {
        Assert.IsFalse(result.IsError is true);
        var payload = ParseTextContent(result);
        Assert.AreEqual("sample.cs", payload.GetProperty("filePath").GetString());
        Assert.AreEqual(4, payload.GetProperty("line").GetInt32());
        Assert.AreEqual(expectedColumn, payload.GetProperty("column").GetInt32());
    }

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
                    Name = "lsp-location-alias-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticSourceLocationTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "lsp-location-alias-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "lsp-location-alias-wire",
            cancellationToken: CancellationToken.None,
            serverOptions: options,
            serverServicesFactory: () => provider);
    }

    [McpServerToolType]
    private sealed class SyntheticSourceLocationTools
    {
        [McpServerTool(Name = ToolName)]
        public static string Locate(string filePath, int line, int? column = null) =>
            JsonSerializer.Serialize(new { filePath, line, column });
    }
}
