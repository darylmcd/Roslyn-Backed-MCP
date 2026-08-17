using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: locks in the wiring for the first batch
/// of read tools that publish an <c>outputSchema</c> per MCP 2025-06-18 § Tools / Structured
/// Content. Each opt-in tool MUST:
/// <list type="number">
///   <item><description>declare an <see cref="McpServerToolAttribute.OutputSchemaType"/>
///     pointing at a real CLR type and opt into structured content;</description></item>
///   <item><description>have its <c>SurfaceEntry.OutputSchema</c> populated by
///     <see cref="ServerSurfaceCatalog"/> via the shared <see cref="ToolOutputSchemaIndex"/>;</description></item>
///   <item><description>publish a JSON-Schema object (<c>type: "object"</c>) at the root;
///     mode-dependent tools must advertise every response variant via <c>anyOf</c> or
///     an exact <c>oneOf</c>.</description></item>
/// </list>
/// Pinning the wiring at the test level means a regression in the index, the catalog factory,
/// or the per-tool annotation surfaces here BEFORE shipping.
/// </summary>
[TestClass]
public sealed class Batch1OutputSchemaTests
{
    private static readonly (string ToolName, Type ExpectedDtoType)[] _adopters =
    [
        ("server_info", typeof(ServerInfoDto)),
        ("server_heartbeat", typeof(ServerHeartbeatDto)),
        ("workspace_status", typeof(WorkspaceStatusSummaryDto)),
        ("workspace_list", typeof(WorkspaceListDto)),
        ("workspace_health", typeof(WorkspaceStatusSummaryDto)),
        ("workspace_drift_check", typeof(WorkspaceDriftResult)),
        ("workspace_readiness_report", typeof(WorkspaceReadinessReportDto)),
        ("workspace_support_bundle", typeof(WorkspaceSupportBundleDto)),
    ];

    [TestMethod]
    public void AllEightAdoptersUseTheSdkStructuredResultContract()
    {
        // The SDK attribute is the sole schema-type owner. Each producer also returns an explicit
        // CallToolResult so the SDK never attempts to structure an already-serialized string.
        var assembly = typeof(RoslynMcp.Host.Stdio.Tools.ServerTools).Assembly;
        var byToolName = new Dictionary<string, (Type? SchemaType, bool Structured, Type ReturnType)>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr?.Name is null) continue;

                byToolName[toolAttr.Name] = (
                    toolAttr.OutputSchemaType,
                    toolAttr.UseStructuredContent,
                    method.ReturnType);
            }
        }

        foreach (var (toolName, expectedDtoType) in _adopters)
        {
            Assert.IsTrue(byToolName.ContainsKey(toolName),
                $"Tool '{toolName}' is not registered (no [McpServerTool] match in the host assembly).");
            var contract = byToolName[toolName];
            Assert.AreEqual(expectedDtoType, contract.SchemaType,
                $"Tool '{toolName}' must declare OutputSchemaType = typeof({expectedDtoType.Name}).");
            Assert.IsTrue(contract.Structured,
                $"Tool '{toolName}' must opt into SDK structured content.");
            Assert.AreEqual(typeof(Task<CallToolResult>), contract.ReturnType,
                $"Tool '{toolName}' must return an explicit producer-owned CallToolResult.");
        }
    }

    [TestMethod]
    public void StructuredResultFactoryRejectsPreSerializedJson()
    {
        using var document = JsonDocument.Parse("{}");

        Assert.ThrowsExactly<ArgumentException>(
            () => RoslynMcp.Host.Stdio.Tools.StructuredToolResult.Create("{}"));
        Assert.ThrowsExactly<ArgumentException>(
            () => RoslynMcp.Host.Stdio.Tools.StructuredToolResult.Create(document));
        Assert.ThrowsExactly<ArgumentException>(
            () => RoslynMcp.Host.Stdio.Tools.StructuredToolResult.Create(document.RootElement));
        Assert.ThrowsExactly<ArgumentException>(
            () => RoslynMcp.Host.Stdio.Tools.StructuredToolResult.Create(JsonNode.Parse("{}")!));
    }

    [TestMethod]
    public void AllEightAdoptersPublishSchemaThroughCatalog()
    {
        // End-to-end check: the catalog's SurfaceEntry.OutputSchema is populated for each
        // batch-1 tool via the static ToolOutputSchemaIndex factory wiring. This proves the
        // attribute → reflection → schema-export → catalog chain stays connected.
        var toolsByName = ServerSurfaceCatalog.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var (toolName, _) in _adopters)
        {
            Assert.IsTrue(toolsByName.TryGetValue(toolName, out var entry),
                $"Tool '{toolName}' is missing from ServerSurfaceCatalog.Tools.");
            Assert.IsNotNull(entry!.OutputSchema,
                $"Tool '{toolName}' must publish a non-null OutputSchema on its catalog entry " +
                "after batch-1 wiring. A null schema means ToolOutputSchemaIndex.GetSchema(name) " +
                "did not find an SDK [McpServerTool(OutputSchemaType = ...)] declaration at static init.");

            var schemaObj = entry.OutputSchema!.AsObject();
            Assert.IsTrue(schemaObj.ContainsKey("type"),
                $"Tool '{toolName}' schema must declare a top-level 'type' field.");
            Assert.AreEqual("object", schemaObj["type"]!.GetValue<string>(),
                $"Tool '{toolName}' schema must be an object (structuredContent shape per MCP spec).");
            Assert.IsTrue(
                schemaObj.ContainsKey("properties")
                || schemaObj.ContainsKey("anyOf")
                || schemaObj.ContainsKey("oneOf"),
                $"Tool '{toolName}' schema must describe structuredContent through either " +
                "a fixed 'properties' object or a mode-dependent variant union.");
        }
    }

    [TestMethod]
    public void ModeDependentWorkspaceToolsAdvertiseEverySerializedDtoVariant()
    {
        AssertUnionVariants(
            "workspace_list",
            "anyOf",
            typeof(WorkspaceListDto),
            typeof(WorkspaceListVerboseDto));
        AssertUnionVariants(
            "workspace_status",
            "oneOf",
            typeof(WorkspaceStatusSummaryDto),
            typeof(WorkspaceStatusDto));

        static void AssertUnionVariants(string toolName, string keyword, params Type[] expectedTypes)
        {
            var schema = ToolOutputSchemaIndex.GetSchema(toolName)!.AsObject();
            var variants = schema[keyword]!.AsArray();
            Assert.HasCount(expectedTypes.Length, variants);

            for (var index = 0; index < expectedTypes.Length; index++)
            {
                var expected = ToolOutputSchemaIndex.GenerateSchema(expectedTypes[index]);
                Assert.IsTrue(JsonNode.DeepEquals(expected, variants[index]),
                    $"Tool '{toolName}' union branch {index} must describe " +
                    $"{expectedTypes[index].Name}, the DTO serialized by that mode.");
            }
        }
    }

    [TestMethod]
    public void RegisteredToolNamesMatchExactlyTheEightAdopters()
    {
        // Reverse direction: ToolOutputSchemaIndex.RegisteredToolNames is the source of truth
        // for "which tools opted in". A future batch-2 PR that drops one of these names would
        // be caught here.
        CollectionAssert.AreEquivalent(
            _adopters.Select(static adopter => adopter.ToolName).ToArray(),
            ToolOutputSchemaIndex.RegisteredToolNames.ToArray());
    }
}
