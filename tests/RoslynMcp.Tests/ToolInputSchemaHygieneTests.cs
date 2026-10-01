using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>workspace-close-schema-leaks-test-seams</c>: the MCP SDK advertises
/// every non-DI parameter of an <c>[McpServerTool]</c> method in <c>tools/list</c>, whether or not
/// it carries a <c>[Description]</c>. A test seam declared on a tool method (a delegate, a
/// <see cref="TimeSpan"/> override) therefore leaks to clients as an undocumented, client-settable
/// input. These tests scan the live registered surface so any such leak fails the build.
/// </summary>
[TestClass]
public sealed class ToolInputSchemaHygieneTests
{
    private const string UnsupportedTypeComment = "Unsupported .NET type";

    [TestMethod]
    public void WorkspaceClose_InputSchema_ExposesOnlyWorkspaceIdAndDrainProcesses()
    {
        using var provider = BuildHostServiceProviderWithTools();
        var tool = provider.GetServices<McpServerTool>().Single(t => t.ProtocolTool.Name == "workspace_close");

        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        CollectionAssert.AreEquivalent(
            new[] { "workspaceId", "drainProcesses" },
            properties.EnumerateObject().Select(p => p.Name).ToArray(),
            $"workspace_close must not advertise internal test seams. Schema: {tool.ProtocolTool.InputSchema}");
    }

    [TestMethod]
    public void EveryTool_InputSchemaProperties_AreSupportedAndDescribed()
    {
        using var provider = BuildHostServiceProviderWithTools();
        var tools = provider.GetServices<McpServerTool>().ToList();
        Assert.IsTrue(tools.Count > 0, "No MCP tools were registered — test fixture broken.");

        var failures = new List<string>();
        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.InputSchema;
            if (!schema.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                var location = $"{tool.ProtocolTool.Name}.{property.Name}";
                if (property.Value.TryGetProperty("$comment", out var comment)
                    && comment.ValueKind == JsonValueKind.String
                    && comment.GetString()!.Contains(UnsupportedTypeComment, StringComparison.Ordinal))
                {
                    failures.Add($"{location}: schema carries $comment \"{comment.GetString()}\" (a non-JSON type leaked into the tool schema).");
                }

                if (!property.Value.TryGetProperty("description", out var description)
                    || description.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(description.GetString()))
                {
                    failures.Add($"{location}: parameter has no description (undocumented input — a leaked seam or a missing [Description]).");
                }
            }
        }

        Assert.AreEqual(0, failures.Count,
            "Tool input schemas must expose only supported, described parameters. Move test seams off " +
            "[McpServerTool] methods onto an internal overload and describe every real input.\n" +
            string.Join("\n", failures));
    }

    [TestMethod]
    public void ToolParameterIndex_MatchesSdkAdvertisedSchemaProperties_ForEveryTool()
    {
        using var provider = BuildHostServiceProviderWithTools();
        var tools = provider.GetServices<McpServerTool>().ToList();
        Assert.IsTrue(tools.Count > 0, "No MCP tools were registered — test fixture broken.");

        var failures = new List<string>();
        foreach (var tool in tools)
        {
            var name = tool.ProtocolTool.Name;
            var schemaNames = tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
                              && properties.ValueKind == JsonValueKind.Object
                ? properties.EnumerateObject().Select(p => p.Name).ToArray()
                : Array.Empty<string>();

            // The index omits tools with no user-facing parameters, so an empty schema must yield an empty set.
            var indexNames = ToolParameterIndex.GetParameters(name).Select(p => p.Name).ToArray();

            // The SDK camel-cases names on the wire; the index keeps the C# casing.
            var normalizedIndex = indexNames.Select(ToCamelCase).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var normalizedSchema = schemaNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!normalizedIndex.SequenceEqual(normalizedSchema, StringComparer.Ordinal))
            {
                failures.Add($"{name}: index=[{string.Join(", ", normalizedIndex)}] schema=[{string.Join(", ", normalizedSchema)}]");
            }
        }

        Assert.AreEqual(0, failures.Count,
            "ToolParameterIndex must select parameters by the SDK-bound schema boundary. If a DI-resolved " +
            "concrete type leaked into the index, add it to ToolParameterIndex.IsUserFacing.\n" +
            string.Join("\n", failures));
    }

    [TestMethod]
    public void ToolParameterIndex_IsUserFacing_IncludesUndescribedBoundParameter_AndExcludesHostSuppliedOnes()
    {
        var parameters = typeof(HygieneFixture)
            .GetMethod(nameof(HygieneFixture.Sample))!
            .GetParameters()
            .ToDictionary(p => p.Name!, StringComparer.Ordinal);

        Assert.IsTrue(ToolParameterIndex.IsUserFacing(parameters["undescribed"]), "A bound parameter without [Description] is still caller input.");
        Assert.IsTrue(ToolParameterIndex.IsUserFacing(parameters["described"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["cancellationToken"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["server"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["context"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["progress"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["gate"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["metadata"]));
        Assert.IsFalse(ToolParameterIndex.IsUserFacing(parameters["options"]));
        Assert.IsTrue(ToolParameterIndex.IsUserFacing(parameters["filePaths"]), "Collection interfaces are JSON-bound caller input.");
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static class HygieneFixture
    {
        public static void Sample(
            string undescribed,
            [System.ComponentModel.Description("d")] string described,
            CancellationToken cancellationToken,
            McpServer server,
            ModelContextProtocol.Server.RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams> context,
            IProgress<ModelContextProtocol.ProgressNotificationValue> progress,
            RoslynMcp.Core.Services.IWorkspaceExecutionGate gate,
            ServerProcessMetadata metadata,
            ValidationServiceOptions options,
            IReadOnlyList<string>? filePaths = null)
        {
        }
    }

    private static ServiceProvider BuildHostServiceProviderWithTools()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions(),
            new ScriptingServiceOptions());
        services
            .AddMcpServer()
            .WithToolsFromAssembly(typeof(HostAssemblyMarker).Assembly);
        return services.BuildServiceProvider();
    }
}
