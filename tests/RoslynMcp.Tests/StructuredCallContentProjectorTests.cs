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
/// filter. These tests call the projector directly (not through the filter's thin delegate) so the
/// extracted collaborator is
/// exercised on its own surface. The delegate-forwarded behavior stays pinned by
/// <see cref="StructuredCallToolFilterTests"/> and <see cref="StructuredContentRoundTripTests"/>.
/// </summary>
[TestClass]
public sealed class StructuredCallContentProjectorTests
{
    [TestMethod]
    public void InjectMetaIntoContent_PascalCaseDtoSerialization_EmitsCamelCaseBodyAndMeta()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new
            {
                WorkspaceId = "ws-1",
                LineCount = 42,
                ProjectCount = 3,
                IsLoaded = true,
            },
            JsonDefaults.Indented);
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "workspace_status");

        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;

        foreach (var property in payload.EnumerateObject())
        {
            if (property.Name != "_meta")
            {
                Assert.IsTrue(char.IsLower(property.Name[0]),
                    $"Top-level response key '{property.Name}' must be camelCase.");
            }
        }

        Assert.IsTrue(payload.TryGetProperty("_meta", out var meta),
            "Object-rooted success responses must carry a _meta block for observability.");
        foreach (var property in meta.EnumerateObject())
        {
            Assert.IsTrue(char.IsLower(property.Name[0]),
                $"_meta key '{property.Name}' must be camelCase.");
        }

        foreach (var requiredField in new[] { "queuedMs", "heldMs", "elapsedMs" })
        {
            Assert.IsTrue(meta.TryGetProperty(requiredField, out _),
                $"_meta.{requiredField} is part of the documented gate-metrics surface.");
        }
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
        var resultMeta = new JsonObject { ["sentinel"] = "result-meta" };
        var input = new CallToolResult
        {
            Meta = resultMeta,
            ResultType = "sentinel-result",
            IsError = true,
            Content = [],
            StructuredContent = ParseElement("null"),
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result,
            "An empty response has no projection target; the complete producer envelope must pass through.");
    }

    [TestMethod]
    public void InjectMetaIntoContent_LeadingNonTextContent_ReturnsResultUnchanged()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var leading = new ImageContentBlock
        {
            Data = new byte[] { 0 },
            MimeType = "image/png",
            Meta = new JsonObject { ["sentinel"] = "image-meta" },
            Annotations = new Annotations { Audience = [Role.Assistant], Priority = 0.5F },
        };
        var input = new CallToolResult
        {
            Meta = new JsonObject { ["sentinel"] = "result-meta" },
            ResultType = "sentinel-result",
            IsError = true,
            Content = [leading, new TextContentBlock { Text = "{\"ignored\":true}" }],
            StructuredContent = ParseElement("[1,2,3]"),
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "sample_tool");

        Assert.AreSame(input, result,
            "Projection is defined only for a leading text block; later text must not reorder content.");
    }

    [TestMethod]
    [DataRow("", DisplayName = "EmptyText")]
    [DataRow("not valid json at all", DisplayName = "NonJsonText")]
    public void InjectMetaIntoContent_NonProjectableText_ReturnsResultUnchanged(string bodyText)
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyText }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result);
    }

    // ── producer-owned structuredContent ──

    [TestMethod]
    public void InjectMetaIntoContent_MissingStructuredContent_DoesNotSynthesizeIt()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(
            new { name = "ws-1", count = 42, loaded = true }, JsonDefaults.Indented);
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "sample_tool");

        // Channel 1: text carries body + _meta.
        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        var textPayload = document.RootElement;
        Assert.IsTrue(textPayload.TryGetProperty("name", out _),
            "Text channel must still carry the response body for legacy clients.");
        Assert.IsTrue(textPayload.TryGetProperty("_meta", out _),
            "Text channel must carry _meta for observability.");

        Assert.IsNull(result.StructuredContent,
            "The decorator must not hide a producer bug by reverse-deserializing its text channel.");
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

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "no_schema_tool");

        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out _),
            "Tools without a schema still get _meta on the text channel — the legacy contract.");
        Assert.IsNull(result.StructuredContent,
            "Tools without a registered output schema MUST NOT populate structuredContent.");
    }

    [TestMethod]
    [DataRow("[1,2,3]", DisplayName = "Array")]
    [DataRow("42", DisplayName = "Number")]
    [DataRow("true", DisplayName = "Boolean")]
    [DataRow("\"value\"", DisplayName = "String")]
    [DataRow("null", DisplayName = "Null")]
    public void InjectMetaIntoContent_NonObjectJsonRoot_ReturnsResultUnchanged(string bodyJson)
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = bodyJson }],
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "sample_tool");

        Assert.AreSame(input, result,
            "The text _meta decorator applies only to object-rooted JSON.");
    }

    [TestMethod]
    [DataRow("{\"producer\":\"object\"}", DisplayName = "Object")]
    [DataRow("[\"producer-array\"]", DisplayName = "Array")]
    [DataRow("42", DisplayName = "Number")]
    [DataRow("\"producer-string\"", DisplayName = "String")]
    [DataRow("true", DisplayName = "Boolean")]
    [DataRow("null", DisplayName = "Null")]
    public void InjectMetaIntoContent_ProducerStructuredContent_PreservesEveryEnvelopeSentinel(
        string structuredJson)
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var bodyJson = JsonSerializer.Serialize(new { name = "ws-1" }, JsonDefaults.Indented);
        var producerStructured = ParseElement(structuredJson);
        var resultMeta = new JsonObject { ["sentinel"] = "result-meta" };
        var textMeta = new JsonObject { ["sentinel"] = "text-meta" };
        var textAnnotations = new Annotations
        {
            Audience = [Role.Assistant],
            Priority = 0.75F,
            LastModified = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
        };
        var first = new TextContentBlock
        {
            Text = bodyJson,
            Meta = textMeta,
            Annotations = textAnnotations,
        };
        var trailing = new TextContentBlock
        {
            Text = "trailing-sentinel",
            Meta = new JsonObject { ["sentinel"] = "trailing-meta" },
            Annotations = new Annotations { Audience = [Role.User], Priority = 0.25F },
        };
        var input = new CallToolResult
        {
            Meta = resultMeta,
            ResultType = "sentinel-result",
            IsError = true,
            Content = [first, trailing],
            StructuredContent = producerStructured,
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(input, "sample_tool");

        Assert.AreSame(input, result, "Projection must preserve the complete SDK result object.");
        Assert.AreSame(resultMeta, result.Meta, "Result-level _meta is producer-owned.");
        Assert.AreEqual("sentinel-result", result.ResultType, "Alternate result discriminator must survive.");
        Assert.IsTrue(result.IsError, "Error state must survive projection.");
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(producerStructured.ValueKind, result.StructuredContent.Value.ValueKind);
        Assert.AreEqual(producerStructured.GetRawText(), result.StructuredContent.Value.GetRawText(),
            "Producer-owned structuredContent must never be replaced by the text-body mirror.");

        Assert.IsNotNull(result.Content);
        Assert.HasCount(2, result.Content, "Content count must not change.");
        var projectedText = (TextContentBlock)result.Content[0];
        Assert.AreSame(first, projectedText, "Projection must preserve the SDK content-block object.");
        Assert.AreSame(textMeta, projectedText.Meta, "First-block protocol metadata must survive.");
        Assert.AreSame(textAnnotations, projectedText.Annotations, "First-block annotations must survive.");
        Assert.AreSame(trailing, result.Content[1], "Trailing blocks must preserve order and identity.");
        Assert.AreNotEqual(bodyJson, first.Text, "The producer block must receive the text decoration.");

        using var projectedDocument = JsonDocument.Parse(projectedText.Text);
        var projectedBody = projectedDocument.RootElement;
        Assert.IsTrue(projectedBody.TryGetProperty("_meta", out _),
            "The intended text-body observability projection must still occur.");

        var projectedWithoutMeta = JsonNode.Parse(projectedText.Text)!.AsObject();
        Assert.IsTrue(projectedWithoutMeta.Remove("_meta"));
        Assert.IsTrue(
            JsonNode.DeepEquals(JsonNode.Parse(bodyJson), projectedWithoutMeta),
            "Projection may add only the text-channel _meta property.");
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
