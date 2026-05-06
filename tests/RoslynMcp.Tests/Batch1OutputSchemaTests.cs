using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: locks in the wiring for the first batch
/// of read tools that publish an <c>outputSchema</c> per MCP 2025-06-18 § Tools / Structured
/// Content. Each opt-in tool MUST:
/// <list type="number">
///   <item><description>declare an <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/>
///     pointing at a real CLR type (so the schema lookup is non-null at static init);</description></item>
///   <item><description>have its <c>SurfaceEntry.OutputSchema</c> populated by
///     <see cref="ServerSurfaceCatalog"/> via the shared <see cref="ToolOutputSchemaIndex"/>;</description></item>
///   <item><description>publish a JSON-Schema object (<c>type: "object"</c>) at the root.</description></item>
/// </list>
/// Pinning the wiring at the test level means a regression in the index, the catalog factory,
/// or the per-tool annotation surfaces here BEFORE shipping.
/// </summary>
[TestClass]
public sealed class Batch1OutputSchemaTests
{
    private static readonly (string ToolName, Type ExpectedDtoType)[] s_batch1 =
    [
        ("server_info", typeof(ServerInfoDto)),
        ("server_heartbeat", typeof(ServerHeartbeatDto)),
        ("workspace_status", typeof(WorkspaceStatusSummaryDto)),
        ("workspace_list", typeof(WorkspaceListDto)),
        ("workspace_health", typeof(WorkspaceStatusSummaryDto)),
        ("workspace_drift_check", typeof(WorkspaceDriftResult)),
    ];

    [TestMethod]
    public void Batch1_AllSixToolsDeclareOutputSchemaTypeRef()
    {
        // Reflection-side check: each tool's [McpToolMetadata] carries a non-null
        // OutputSchemaTypeRef whose value matches the expected DTO type. If a tool body is
        // refactored away from its DTO without updating the annotation (or vice versa), this
        // test catches the drift before ToolOutputSchemaIndex silently regresses to "no schema".
        var assembly = typeof(RoslynMcp.Host.Stdio.Tools.ServerTools).Assembly;
        var byToolName = new Dictionary<string, Type?>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr?.Name is null) continue;

                var metadataAttr = method.GetCustomAttribute<McpToolMetadataAttribute>();
                byToolName[toolAttr.Name] = metadataAttr?.OutputSchemaTypeRef;
            }
        }

        foreach (var (toolName, expectedDtoType) in s_batch1)
        {
            Assert.IsTrue(byToolName.ContainsKey(toolName),
                $"Tool '{toolName}' is not registered (no [McpServerTool] match in the host assembly).");
            Assert.AreEqual(expectedDtoType, byToolName[toolName],
                $"Tool '{toolName}' must declare outputSchemaTypeRef = typeof({expectedDtoType.Name}) " +
                $"in its [McpToolMetadata] annotation.");
        }
    }

    [TestMethod]
    public void Batch1_AllSixToolsPublishSchemaThroughCatalog()
    {
        // End-to-end check: the catalog's SurfaceEntry.OutputSchema is populated for each
        // batch-1 tool via the static ToolOutputSchemaIndex factory wiring. This proves the
        // attribute → reflection → schema-export → catalog chain stays connected.
        var toolsByName = ServerSurfaceCatalog.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var (toolName, _) in s_batch1)
        {
            Assert.IsTrue(toolsByName.TryGetValue(toolName, out var entry),
                $"Tool '{toolName}' is missing from ServerSurfaceCatalog.Tools.");
            Assert.IsNotNull(entry!.OutputSchema,
                $"Tool '{toolName}' must publish a non-null OutputSchema on its catalog entry " +
                "after batch-1 wiring. A null schema means ToolOutputSchemaIndex.GetSchema(name) " +
                "did not find a [McpToolMetadata(outputSchemaTypeRef:)] annotation at static init.");

            var schemaObj = entry.OutputSchema!.AsObject();
            Assert.IsTrue(schemaObj.ContainsKey("type"),
                $"Tool '{toolName}' schema must declare a top-level 'type' field.");
            Assert.AreEqual("object", schemaObj["type"]!.GetValue<string>(),
                $"Tool '{toolName}' schema must be an object (structuredContent shape per MCP spec).");
            Assert.IsTrue(schemaObj.ContainsKey("properties"),
                $"Tool '{toolName}' schema must declare a 'properties' field describing the " +
                "structuredContent shape.");
        }
    }

    [TestMethod]
    public void Batch1_RegisteredToolNamesContainAllSixTools()
    {
        // Reverse direction: ToolOutputSchemaIndex.RegisteredToolNames is the source of truth
        // for "which tools opted in". A future batch-2 PR that drops one of these names would
        // be caught here.
        var registered = ToolOutputSchemaIndex.RegisteredToolNames;
        foreach (var (toolName, _) in s_batch1)
        {
            CollectionAssert.Contains(registered.ToArray(), toolName,
                $"Tool '{toolName}' must appear in ToolOutputSchemaIndex.RegisteredToolNames.");
        }
    }
}
