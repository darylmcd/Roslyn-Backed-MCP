using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio;
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
    public void EverySdkAdopterPairsWithExactlyOneCatalogDeclaration()
    {
        // Authority symmetry, asserted statically. ToolOutputSchemaIndex fails closed on either
        // asymmetry at index construction; this test names the offending side when it does, and
        // fails on the CI machine rather than on a client's first tools/list.
        var sdkDeclared = ToolOutputSchemaIndex.SdkDeclaredOutputSchemaTypes;

        CollectionAssert.AreEquivalent(
            ToolOutputSchemaIndex.Declarations.Keys.ToArray(),
            sdkDeclared.Keys.ToArray(),
            "Every [McpServerTool(OutputSchemaType = ...)] adopter must carry exactly one explicit " +
            "OutputSchemaDeclaration, and every declaration must name a live adopter. One generation " +
            "authority means neither side may drift alone.");

        foreach (var (toolName, expectedDtoType) in _adopters)
        {
            Assert.AreEqual(expectedDtoType, sdkDeclared[toolName],
                $"Tool '{toolName}' must keep OutputSchemaType = typeof({expectedDtoType.Name}); " +
                "the catalog derives its advertised schema from that declared type.");
        }
    }

    [TestMethod]
    public void FixedVersusUnionMatrixMatchesEachToolsDeclaredGenerationRoute()
    {
        // The matrix: for every adopter, compare the SDK-discovered DTO type, its declared
        // generation route, and the schema the catalog actually advertises.
        var sdkDeclared = ToolOutputSchemaIndex.SdkDeclaredOutputSchemaTypes;

        foreach (var (toolName, declaration) in ToolOutputSchemaIndex.Declarations)
        {
            var declaredDtoType = sdkDeclared[toolName];
            var advertised = ToolOutputSchemaIndex.GetSchema(toolName);
            Assert.IsNotNull(advertised, $"Tool '{toolName}' must publish an advertised schema.");
            var advertisedObject = advertised!.AsObject();
            var variants = declaration.Variants(declaredDtoType);

            if (declaration.Kind == OutputSchemaKind.Fixed)
            {
                Assert.IsNull(declaration.UnionKeyword,
                    $"Tool '{toolName}' is declared Fixed and must not name a union keyword.");
                Assert.HasCount(1, variants);
                Assert.IsFalse(
                    advertisedObject.ContainsKey("anyOf") || advertisedObject.ContainsKey("oneOf"),
                    $"Tool '{toolName}' is declared Fixed, so its advertised schema must be the DTO " +
                    "shape verbatim — a union here means the declaration and the generator disagree.");
                Assert.IsTrue(
                    JsonNode.DeepEquals(ToolOutputSchemaIndex.GenerateSchema(declaredDtoType), advertised),
                    $"Tool '{toolName}' advertises a schema that is not the generated shape of " +
                    $"{declaredDtoType.Name}. A Fixed declaration must not reshape the DTO.");
                continue;
            }

            Assert.IsNotNull(declaration.UnionKeyword,
                $"Tool '{toolName}' is declared Union and must name anyOf or oneOf.");
            Assert.AreEqual("object", advertisedObject["type"]!.GetValue<string>(),
                $"Tool '{toolName}' union schema must still declare the outer structuredContent object type.");

            var branches = advertisedObject[declaration.UnionKeyword!]!.AsArray();
            Assert.HasCount(variants.Count, branches);
            Assert.AreEqual(declaredDtoType, variants[0],
                $"Tool '{toolName}' union branch 0 must be the SDK-declared DTO so the declaration " +
                "cannot silently diverge from the attribute.");

            for (var index = 0; index < variants.Count; index++)
            {
                Assert.IsTrue(
                    JsonNode.DeepEquals(ToolOutputSchemaIndex.GenerateSchema(variants[index]), branches[index]),
                    $"Tool '{toolName}' union branch {index} must describe {variants[index].Name}, " +
                    "the DTO serialized by that mode.");
            }
        }
    }

    [TestMethod]
    public void AdvertisedSchemasUseTheRuntimeStructuredContentSerializerMetadata()
    {
        // Acceptance for "prove custom catalog schemas use the same serializer metadata as SDK
        // runtime output": the catalog overwrites the SDK-generated schema precisely because the
        // SDK generator runs on its own defaults. That overwrite is only defensible while the
        // advertised property names are the names JsonDefaults.Indented — the options object the
        // structuredContent channel serializes with — actually emits.
        _ = JsonSerializer.Serialize(new { probe = 0 }, JsonDefaults.Indented);

        foreach (var (toolName, declaration) in ToolOutputSchemaIndex.Declarations)
        {
            var declaredDtoType = ToolOutputSchemaIndex.SdkDeclaredOutputSchemaTypes[toolName];
            var variants = declaration.Variants(declaredDtoType);
            var advertised = ToolOutputSchemaIndex.GetSchema(toolName)!.AsObject();

            JsonObject[] branches = declaration.Kind == OutputSchemaKind.Fixed
                ? [advertised]
                : advertised[declaration.UnionKeyword!]!.AsArray().Select(node => node!.AsObject()).ToArray();

            for (var index = 0; index < variants.Count; index++)
            {
                var runtimeNames = JsonDefaults.Indented.GetTypeInfo(variants[index])
                    .Properties
                    .Select(static property => property.Name)
                    .ToArray();
                var advertisedNames = branches[index]["properties"]!.AsObject()
                    .Select(static property => property.Key)
                    .ToArray();

                CollectionAssert.AreEquivalent(runtimeNames, advertisedNames,
                    $"Tool '{toolName}' branch {index} ({variants[index].Name}) advertises property names " +
                    "that JsonDefaults.Indented would not emit at runtime. The advertised schema and the " +
                    "structuredContent bytes must come from one serializer-metadata source.");
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
