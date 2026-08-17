using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-output-schema-infrastructure: locks in MCP 2025-06-18 § Tools / Structured Content
/// behavior. Tools that declare an output schema via
/// <see cref="McpServerToolAttribute.OutputSchemaType"/> emit BOTH the legacy
/// <c>content[].text</c> channel AND the new <c>structuredContent</c> channel; tools that do
/// not declare a schema continue to emit text-only (unchanged contract). The <c>_meta</c>
/// observability blob lives only in the text channel — clients never see two of them.
/// </summary>
[TestClass]
public sealed class StructuredContentRoundTripTests
{
    private sealed record SampleDto(string Name, int Count, bool Loaded);

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
    public void ProducerOwnedResult_RoundTripsThroughDecoratorWithoutChannelDrift()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var payload = new SampleDto("ws-1", 42, true);
        var input = StructuredToolResult.Create(payload);
        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "sample_tool");

        // Channel 1: text content carries the JSON body PLUS _meta.
        var text = ((TextContentBlock)result.Content![0]).Text;
        using var textDocument = JsonDocument.Parse(text);
        var textPayload = textDocument.RootElement;
        Assert.IsTrue(textPayload.TryGetProperty("name", out _),
            "Text channel must still carry the response body for legacy clients.");
        Assert.IsTrue(textPayload.TryGetProperty("_meta", out _),
            "Text channel must carry _meta for observability — this is the dedupe site.");

        // Channel 2: structuredContent carries the same body MINUS _meta.
        Assert.IsNotNull(result.StructuredContent);
        var structured = result.StructuredContent.Value;
        Assert.IsTrue(structured.TryGetProperty("name", out var nameProp));
        Assert.AreEqual("ws-1", nameProp.GetString());
        var schema = ToolOutputSchemaIndex.GenerateSchema(typeof(SampleDto)).AsObject();
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
        Assert.AreEqual(
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Indented).GetRawText(),
            structured.GetRawText(),
            "The producer's typed payload must remain the exact structured-channel owner.");
    }
}
