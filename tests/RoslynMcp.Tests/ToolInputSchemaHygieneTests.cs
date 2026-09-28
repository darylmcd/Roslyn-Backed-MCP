using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
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
