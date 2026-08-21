using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class GeneratedJsonSchemaMatcherTests
{
    private sealed record RecursiveDto(int Value, RecursiveDto? Next);

    [TestMethod]
    public void Matches_SupportedAssertionsRejectNonmatchingValue()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "required": ["items"],
              "properties": {
                "items": {
                  "type": "array",
                  "minItems": 2,
                  "maxItems": 3,
                  "items": { "type": "integer" }
                }
              },
              "additionalProperties": false
            }
            """)!;
        using var value = JsonDocument.Parse("""{"items":[1]}""");

        Assert.IsFalse(GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }

    [TestMethod]
    public void Matches_UnsupportedAssertionInUnvisitedProperty_Throws()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "optionalName": {
                  "type": "string",
                  "minLength": 5
                }
              }
            }
            """)!;
        using var value = JsonDocument.Parse("{}");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "minLength");
        StringAssert.Contains(thrown.Message, "#/properties/optionalName");
    }

    [TestMethod]
    public void Matches_AnnotationOnlyKeywordsDoNotAssertAgainstValue()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "string",
              "$comment": "Annotations are deliberately ignored by this matcher.",
              "title": "Display name",
              "description": "A value whose format is advisory under the default vocabulary.",
              "default": "fallback",
              "deprecated": false,
              "examples": ["https://example.test"],
              "format": "uri",
              "readOnly": false,
              "writeOnly": false
            }
            """)!;
        using var value = JsonDocument.Parse("\"not a URI\"");

        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }

    [TestMethod]
    [DataRow("https://json-schema.org/draft/2020-12/schema")]
    [DataRow("https://json-schema.org/draft/2020-12/schema#")]
    public void Matches_JsonSchema202012Dialect_IsAccepted(string dialect)
    {
        var schema = new JsonObject
        {
            ["$schema"] = dialect,
            ["type"] = "string",
        };
        using var value = JsonDocument.Parse("\"value\"");

        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }

    [TestMethod]
    [DataRow("\"https://json-schema.org/draft/2019-09/schema\"")]
    [DataRow("42")]
    public void Matches_UnsupportedOrMalformedDialect_Throws(string dialectJson)
    {
        var schema = JsonNode.Parse($$"""{"$schema":{{dialectJson}}}""")!;
        using var value = JsonDocument.Parse("null");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "$schema");
        StringAssert.Contains(thrown.Message, "2020-12");
    }

    [TestMethod]
    public void Matches_LocalReferenceStillEvaluatesSiblingAssertions()
    {
        var schema = JsonNode.Parse(
            """
            {
              "$defs": {
                "identifier": { "type": "string" }
              },
              "$ref": "#/$defs/identifier",
              "const": "expected"
            }
            """)!;
        using var value = JsonDocument.Parse("\"other\"");

        Assert.IsFalse(GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }

    [TestMethod]
    public void Matches_UnresolvedLocalReference_Throws()
    {
        var schema = JsonNode.Parse("""{"$ref":"#/$defs/missing"}""")!;
        using var value = JsonDocument.Parse("null");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "does not resolve");
    }

    [TestMethod]
    public void Matches_ReferenceTargetIsPreflightValidated()
    {
        var schema = JsonNode.Parse(
            """
            {
              "default": { "minLength": 5 },
              "$ref": "#/default"
            }
            """)!;
        using var value = JsonDocument.Parse("\"x\"");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "minLength");
        StringAssert.Contains(thrown.Message, "#/default");
    }

    [TestMethod]
    public void Matches_SelfReferentialSchema_ThrowsDeterministically()
    {
        var schema = JsonNode.Parse("""{"$ref":"#"}""")!;
        using var value = JsonDocument.Parse("null");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "Cyclic local JSON-Schema reference");
        StringAssert.Contains(thrown.Message, "value at '#'");
    }

    [TestMethod]
    public void Matches_IndirectReferenceCycle_ThrowsDeterministically()
    {
        var schema = JsonNode.Parse(
            """
            {
              "$defs": {
                "left": { "$ref": "#/$defs/right" },
                "right": { "$ref": "#/$defs/left" }
              },
              "$ref": "#/$defs/left"
            }
            """)!;
        using var value = JsonDocument.Parse("null");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));

        StringAssert.Contains(thrown.Message, "Cyclic local JSON-Schema reference");
    }

    [TestMethod]
    public void Matches_FiniteRecursiveSchema_EvaluatesEachNestedValue()
    {
        var schema = ToolOutputSchemaIndex.GenerateSchema(typeof(RecursiveDto));
        var matchingValue = JsonSerializer.SerializeToElement(
            new RecursiveDto(1, new RecursiveDto(2, null)),
            JsonDefaults.Indented);
        var nonmatchingValue = JsonSerializer.SerializeToElement(
            new { value = 1, next = new { value = "not-an-integer", next = (object?)null } },
            JsonDefaults.Indented);

        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(matchingValue, schema));
        Assert.IsFalse(GeneratedJsonSchemaMatcher.Matches(nonmatchingValue, schema));
    }

    [TestMethod]
    public void Matches_NullConst_DistinguishesPresentKeywordFromMissingKeyword()
    {
        var schema = JsonNode.Parse("""{"const":null}""")!;
        using var matchingValue = JsonDocument.Parse("null");
        using var nonmatchingValue = JsonDocument.Parse("false");

        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(matchingValue.RootElement, schema));
        Assert.IsFalse(GeneratedJsonSchemaMatcher.Matches(nonmatchingValue.RootElement, schema));
    }

    [TestMethod]
    [DataRow("{\"type\":null}")]
    [DataRow("{\"type\":[]}")]
    [DataRow("{\"required\":\"name\"}")]
    [DataRow("{\"enum\":null}")]
    [DataRow("{\"enum\":[]}")]
    [DataRow("{\"minItems\":-1}")]
    [DataRow("{\"maxItems\":1.5}")]
    [DataRow("{\"allOf\":[]}")]
    [DataRow("{\"anyOf\":[]}")]
    [DataRow("{\"oneOf\":[]}")]
    [DataRow("{\"deprecated\":\"false\"}")]
    public void Matches_MalformedSupportedKeyword_Throws(string schemaJson)
    {
        var schema = JsonNode.Parse(schemaJson)!;
        using var value = JsonDocument.Parse("null");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("1.0", true)]
    [DataRow("100e-2", true)]
    [DataRow("1e1000", true)]
    [DataRow("9223372036854775808", true)]
    [DataRow("-0.000e-999999", true)]
    [DataRow("1.5", false)]
    [DataRow("1e-2", false)]
    public void Matches_IntegerType_UsesMathematicalValue(string numberJson, bool expected)
    {
        var schema = JsonNode.Parse("""{"type":"integer"}""")!;
        using var value = JsonDocument.Parse(numberJson);

        Assert.AreEqual(expected, GeneratedJsonSchemaMatcher.Matches(value.RootElement, schema));
    }
}
