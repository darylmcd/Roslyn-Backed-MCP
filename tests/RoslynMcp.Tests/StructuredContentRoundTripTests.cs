using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-output-schema-infrastructure: locks in MCP 2025-06-18 § Tools / Structured Content
/// behavior. Tools that declare an output schema via
/// <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/> emit BOTH the legacy
/// <c>content[].text</c> channel AND the new <c>structuredContent</c> channel; tools that do
/// not declare a schema continue to emit text-only (unchanged contract). The <c>_meta</c>
/// observability blob lives only in the text channel — clients never see two of them.
/// </summary>
[TestClass]
public sealed class StructuredContentRoundTripTests
{
    private sealed record SampleDto(string Name, int Count, bool Loaded);

    private static JsonNode? ResolveSchemaForSample(string toolName) =>
        toolName == "sample_tool"
            ? ToolOutputSchemaIndex.GenerateSchema(typeof(SampleDto))
            : null;

    private static JsonNode? AlwaysNull(string _) => null;

    [TestMethod]
    public void GenerateSchema_RecordWithInitOnlyProperties_RoundTripsCleanly()
    {
        // Risk (2) from the plan: confirm DTO records (init-only, nested) round-trip cleanly
        // through System.Text.Json.Schema.JsonSchemaExporter. If this assertion ever fails,
        // the JsonSchema.Net package must be added per the Approach addendum.
        var schema = ToolOutputSchemaIndex.GenerateSchema(typeof(SampleDto));

        Assert.IsNotNull(schema);
        var schemaObj = schema.AsObject();
        Assert.AreEqual("object", schemaObj["type"]!.GetValue<string>(),
            "Top-level schema for a record must be of type 'object'.");

        var properties = schemaObj["properties"]!.AsObject();
        Assert.IsTrue(properties.ContainsKey("name"), "Record's Name property must be camel-cased in schema.");
        Assert.IsTrue(properties.ContainsKey("count"), "Record's Count property must be camel-cased in schema.");
        Assert.IsTrue(properties.ContainsKey("loaded"), "Record's Loaded property must be camel-cased in schema.");
    }

    [TestMethod]
    public void InjectMetaIntoContent_ToolWithSchema_EmitsBothChannelsWithSingleMeta()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new { name = "ws-1", count = 42, loaded = true },
            JsonDefaults.Indented);

        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(
            input, "sample_tool", ResolveSchemaForSample);

        // Channel 1: text content carries the JSON body PLUS _meta.
        var text = ((TextContentBlock)result.Content![0]).Text;
        var textPayload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(textPayload.TryGetProperty("name", out _),
            "Text channel must still carry the response body for legacy clients.");
        Assert.IsTrue(textPayload.TryGetProperty("_meta", out _),
            "Text channel must carry _meta for observability — this is the dedupe site.");

        // Channel 2: structuredContent carries the same body MINUS _meta.
        Assert.IsNotNull(result.StructuredContent,
            "Tools with a registered output schema MUST populate structuredContent " +
            "per MCP 2025-06-18 § Tools / Structured Content.");
        var structured = result.StructuredContent.Value;
        Assert.IsTrue(structured.TryGetProperty("name", out var nameProp));
        Assert.AreEqual("ws-1", nameProp.GetString());
        var schema = ResolveSchemaForSample("sample_tool")!.AsObject();
        var schemaKeys = schema["properties"]!.AsObject()
            .Select(property => property.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var structuredKeys = structured.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(schemaKeys, structuredKeys,
            "Published outputSchema property names must exactly match the camelCase structuredContent payload keys.");
        Assert.IsFalse(structured.TryGetProperty("_meta", out _),
            "_meta MUST live ONLY in the text channel — duplicating it into structuredContent " +
            "would surface two observability blobs to the client (dedupe risk per plan).");
    }

    [TestMethod]
    public void InjectMetaIntoContent_ToolWithoutSchema_TextOnlyContractPreserved()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new { name = "ws-1", count = 42 }, JsonDefaults.Indented);

        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(
            input, "no_schema_tool", AlwaysNull);

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out _),
            "Tools without a schema still get _meta on the text channel — the legacy contract.");
        Assert.IsNull(result.StructuredContent,
            "Tools without a registered output schema MUST NOT populate structuredContent — " +
            "emitting an unconstrained body there would violate the spec's schema-conformance " +
            "expectation and break clients that rely on the absence as a signal.");
    }

    [TestMethod]
    public void InjectMetaIntoContent_ArrayRootedJson_StaysTextOnlyEvenWithSchema()
    {
        // Defensive: even if a schema is registered, an array-rooted body cannot be mirrored
        // into structuredContent (which is object-shaped per spec). The filter must NOT
        // fabricate a wrapper — the legacy bare-array contract wins, matching the
        // source_generated_documents pass-through behavior.
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "[1,2,3]" }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(
            input, "sample_tool", ResolveSchemaForSample);

        Assert.AreSame(input, result,
            "Array-rooted responses pass through unchanged regardless of schema registration.");
        Assert.IsNull(result.StructuredContent);
    }

    [TestMethod]
    public void InjectMetaIntoContent_PreExistingStructuredContent_NotOverwritten()
    {
        // If a tool ever sets StructuredContent itself (current production tools don't, but
        // the SDK contract permits it), the filter must NOT clobber it. The dual-channel
        // injection only fills in when the tool left it null.
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new { name = "ws-1" }, JsonDefaults.Indented);
        var preset = JsonDocument.Parse("""{"preset":true}""").RootElement.Clone();

        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
            StructuredContent = preset,
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(
            input, "sample_tool", ResolveSchemaForSample);

        Assert.IsNotNull(result.StructuredContent);
        Assert.IsTrue(result.StructuredContent.Value.TryGetProperty("preset", out _),
            "Pre-existing StructuredContent must be preserved verbatim.");
        Assert.IsFalse(result.StructuredContent.Value.TryGetProperty("name", out _),
            "Filter must NOT overwrite a tool's own StructuredContent.");
    }
}
