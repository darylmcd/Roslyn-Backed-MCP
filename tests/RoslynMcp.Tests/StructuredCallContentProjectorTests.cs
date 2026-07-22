using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct coverage for <see cref="StructuredCallContentProjector"/>, the structured-content /
/// <c>_meta</c> projection layer extracted from <see cref="StructuredCallToolFilter"/> by the
/// <c>structuredcalltoolfilter-hotspot-decomposition-followup</c> initiative. These tests call the
/// projector directly (not through the filter's thin delegate) so the extracted collaborator is
/// exercised on its own surface. The delegate-forwarded behavior stays pinned by
/// <see cref="StructuredCallToolFilterTests"/> and <see cref="StructuredContentRoundTripTests"/>.
/// </summary>
[TestClass]
public sealed class StructuredCallContentProjectorTests
{
    // ── _meta injection on the success path (mirrors StructuredCallToolFilterTests) ──

    [TestMethod]
    public void InjectMetaIntoContent_ObjectRootedJson_InjectsMetaField()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = """{"result":"ok"}""" }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out var meta),
            "Object-rooted success responses must carry a _meta block for observability.");
        Assert.IsTrue(meta.TryGetProperty("queuedMs", out _));
    }

    [TestMethod]
    public void InjectMetaIntoContent_ArrayRootedJson_ReturnsResultUnchanged()
    {
        // Backward-compat contract: tools like source_generated_documents return bare
        // arrays. The projector must NOT wrap them in {data, _meta} — array-rooted JSON
        // passes through byte-for-byte identical (same instance).
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "[1,2,3]" }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result, "Array-rooted responses should return the exact same CallToolResult instance.");
        Assert.AreEqual("[1,2,3]", ((TextContentBlock)result.Content![0]).Text);
    }

    [TestMethod]
    public void InjectMetaIntoContent_EmptyContent_ReturnsResultUnchanged()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result);
    }

    [TestMethod]
    public void InjectMetaIntoContent_NonJsonText_ReturnsResultUnchanged()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "not valid json at all" }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result);
    }

    // ── dual-channel structuredContent via the schema-resolver seam ──

    [TestMethod]
    public void InjectMetaIntoContent_ToolWithSchema_EmitsBothChannelsWithSingleMeta()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new { name = "ws-1", count = 42, loaded = true }, JsonDefaults.Indented);
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(
            input, "sample_tool", _ => JsonNode.Parse("""{"type":"object"}"""));

        // Channel 1: text carries body + _meta.
        var text = ((TextContentBlock)result.Content![0]).Text;
        var textPayload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(textPayload.TryGetProperty("name", out _),
            "Text channel must still carry the response body for legacy clients.");
        Assert.IsTrue(textPayload.TryGetProperty("_meta", out _),
            "Text channel must carry _meta for observability.");

        // Channel 2: structuredContent carries body MINUS _meta.
        Assert.IsNotNull(result.StructuredContent,
            "Tools with a registered output schema MUST populate structuredContent.");
        var structured = result.StructuredContent!.Value;
        Assert.IsTrue(structured.TryGetProperty("name", out var nameProp));
        Assert.AreEqual("ws-1", nameProp.GetString());
        Assert.IsFalse(structured.TryGetProperty("_meta", out _),
            "_meta MUST live ONLY in the text channel — never duplicated into structuredContent.");
    }

    [TestMethod]
    public void InjectMetaIntoContent_ToolWithoutSchema_TextOnlyContractPreserved()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(new { name = "ws-1", count = 42 }, JsonDefaults.Indented);
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(
            input, "no_schema_tool", _ => null);

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out _),
            "Tools without a schema still get _meta on the text channel — the legacy contract.");
        Assert.IsNull(result.StructuredContent,
            "Tools without a registered output schema MUST NOT populate structuredContent.");
    }

    [TestMethod]
    public void InjectMetaIntoContent_ArrayRootedJson_StaysTextOnlyEvenWithSchema()
    {
        // Even when a schema resolves, an array-rooted body cannot be mirrored into the
        // object-shaped structuredContent channel — it stays text-only and unchanged.
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "[1,2,3]" }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(
            input, "sample_tool", _ => JsonNode.Parse("""{"type":"object"}"""));

        Assert.AreSame(input, result);
        Assert.IsNull(result.StructuredContent);
    }

    [TestMethod]
    public void InjectMetaIntoContent_PreExistingStructuredContent_NotOverwritten()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var preExisting = JsonDocument.Parse("""{"pre":"existing"}""").RootElement.Clone();
        var bodyJson = JsonSerializer.Serialize(new { name = "ws-1" }, JsonDefaults.Indented);
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
            StructuredContent = preExisting,
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(
            input, "sample_tool", _ => JsonNode.Parse("""{"type":"object"}"""));

        Assert.IsNotNull(result.StructuredContent);
        Assert.IsTrue(result.StructuredContent!.Value.TryGetProperty("pre", out var pre),
            "A tool that set StructuredContent directly must have it preserved, not overwritten by the body mirror.");
        Assert.AreEqual("existing", pre.GetString());
    }
}
