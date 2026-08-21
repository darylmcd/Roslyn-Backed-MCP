using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;

namespace RoslynMcp.Tests;

/// <summary>
/// Evaluates the explicitly supported JSON-Schema 2020-12 keywords emitted by
/// <see cref="System.Text.Json.Schema.JsonSchemaExporter"/> for this test suite. Unsupported
/// keywords fail before value evaluation, and local references are resolved against the advertised
/// root schema, so a schema change cannot false-green by falling through an unknown constraint.
/// </summary>
internal static class GeneratedJsonSchemaMatcher
{
    private const string Draft202012SchemaUri =
        "https://json-schema.org/draft/2020-12/schema";

    private static readonly HashSet<string> _supportedJsonTypes = new(StringComparer.Ordinal)
    {
        "array",
        "boolean",
        "integer",
        "null",
        "number",
        "object",
        "string",
    };

    private static readonly HashSet<string> _supportedValidationKeywords = new(StringComparer.Ordinal)
    {
        "$ref",
        "additionalProperties",
        "allOf",
        "anyOf",
        "const",
        "enum",
        "items",
        "maxItems",
        "minItems",
        "not",
        "oneOf",
        "properties",
        "required",
        "type",
    };

    private static readonly HashSet<string> _supportedSchemaStructureKeywords = new(StringComparer.Ordinal)
    {
        "$defs",
        "$schema",
    };

    // These JSON Schema 2020-12 keywords collect annotations under the vocabularies used by
    // JsonSchemaExporter. They deliberately do not affect validation here. In particular,
    // `format` is annotation-only unless the format-assertion vocabulary is explicitly enabled;
    // this matcher rejects the unsupported `$vocabulary` keyword rather than guessing.
    private static readonly HashSet<string> _annotationOnlyKeywords = new(StringComparer.Ordinal)
    {
        "$comment",
        "default",
        "deprecated",
        "description",
        "examples",
        "format",
        "readOnly",
        "title",
        "writeOnly",
    };

    public static bool Matches(JsonElement value, JsonNode schema)
    {
        ValidateSupportedSchema(
            schema,
            schema,
            "#",
            new HashSet<JsonNode>(ReferenceEqualityComparer.Instance));
        return MatchesCore(value, schema, schema, new EvaluationContext(), "#");
    }

    private static bool MatchesCore(
        JsonElement value,
        JsonNode schema,
        JsonNode rootSchema,
        EvaluationContext context,
        string valueLocation)
    {
        if (schema is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out var allowed))
        {
            return allowed;
        }

        if (schema is not JsonObject schemaObject)
        {
            throw new InvalidOperationException("Expected an object or boolean JSON schema.");
        }

        if (!MatchesReference(value, schemaObject, rootSchema, context, valueLocation)
            || !MatchesCompositions(value, schemaObject, rootSchema, context, valueLocation)
            || !MatchesDirectAssertions(value, schemaObject))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => MatchesObject(value, schemaObject, rootSchema, context, valueLocation),
            JsonValueKind.Array => MatchesArray(value, schemaObject, rootSchema, context, valueLocation),
            _ => true,
        };
    }

    private static bool MatchesReference(
        JsonElement value,
        JsonObject schemaObject,
        JsonNode rootSchema,
        EvaluationContext context,
        string valueLocation)
    {
        if (schemaObject["$ref"] is not JsonValue referenceNode
            || !referenceNode.TryGetValue<string>(out var reference))
        {
            return true;
        }

        var referencedSchema = ResolveReference(rootSchema, reference);
        if (!context.TryEnterReference(referencedSchema, valueLocation))
        {
            throw new InvalidOperationException(
                $"Cyclic local JSON-Schema reference '{reference}' re-entered the same schema " +
                $"while evaluating value at '{valueLocation}'.");
        }

        try
        {
            return MatchesCore(value, referencedSchema, rootSchema, context, valueLocation);
        }
        finally
        {
            context.ExitReference(referencedSchema, valueLocation);
        }
    }

    private static bool MatchesCompositions(
        JsonElement value,
        JsonObject schemaObject,
        JsonNode rootSchema,
        EvaluationContext context,
        string valueLocation)
    {
        if (schemaObject["allOf"] is JsonArray allOf
            && !allOf.All(candidate => candidate is not null
                && MatchesCore(value, candidate, rootSchema, context, valueLocation)))
        {
            return false;
        }

        if (schemaObject["oneOf"] is JsonArray oneOf
            && oneOf.Count(candidate => candidate is not null
                && MatchesCore(value, candidate, rootSchema, context, valueLocation)) != 1)
        {
            return false;
        }

        if (schemaObject["anyOf"] is JsonArray anyOf
            && !anyOf.Any(candidate => candidate is not null
                && MatchesCore(value, candidate, rootSchema, context, valueLocation)))
        {
            return false;
        }

        if (schemaObject["not"] is { } notSchema
            && MatchesCore(value, notSchema, rootSchema, context, valueLocation))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesDirectAssertions(JsonElement value, JsonObject schemaObject)
    {
        if (schemaObject["type"] is { } typeNode && !MatchesJsonType(value, typeNode))
        {
            return false;
        }

        var hasConst = schemaObject.TryGetPropertyValue("const", out var constNode);
        var enumValues = schemaObject["enum"] as JsonArray;
        if (!hasConst && enumValues is null)
        {
            return true;
        }

        var valueNode = JsonNode.Parse(value.GetRawText());
        if (hasConst
            && !JsonNode.DeepEquals(valueNode, constNode))
        {
            return false;
        }

        if (enumValues is not null
            && !enumValues.Any(candidate => JsonNode.DeepEquals(valueNode, candidate)))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesObject(
        JsonElement value,
        JsonObject schema,
        JsonNode rootSchema,
        EvaluationContext context,
        string valueLocation)
    {
        var properties = schema["properties"] as JsonObject;
        if (schema["required"] is JsonArray required)
        {
            foreach (var requiredName in required.Select(static node => node?.GetValue<string>()))
            {
                if (requiredName is not null && !value.TryGetProperty(requiredName, out _))
                {
                    return false;
                }
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (properties?[property.Name] is { } propertySchema)
            {
                var propertyLocation =
                    $"{valueLocation}/{EscapeJsonPointerSegment(property.Name)}";
                if (!MatchesCore(
                        property.Value,
                        propertySchema,
                        rootSchema,
                        context,
                        propertyLocation))
                {
                    return false;
                }

                continue;
            }

            if (schema["additionalProperties"] is JsonValue additionalProperties
                && additionalProperties.TryGetValue<bool>(out var allowsAdditional)
                && !allowsAdditional)
            {
                return false;
            }

            if (schema["additionalProperties"] is JsonObject additionalPropertySchema
                && !MatchesCore(
                    property.Value,
                    additionalPropertySchema,
                    rootSchema,
                    context,
                    $"{valueLocation}/{EscapeJsonPointerSegment(property.Name)}"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesArray(
        JsonElement value,
        JsonObject schema,
        JsonNode rootSchema,
        EvaluationContext context,
        string valueLocation)
    {
        var itemCount = value.GetArrayLength();
        if (schema["minItems"]?.GetValue<int>() is int minimum && itemCount < minimum)
        {
            return false;
        }

        if (schema["maxItems"]?.GetValue<int>() is int maximum && itemCount > maximum)
        {
            return false;
        }

        if (schema["items"] is { } itemSchema)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (!MatchesCore(
                        item,
                        itemSchema,
                        rootSchema,
                        context,
                        $"{valueLocation}/{index}"))
                {
                    return false;
                }

                index++;
            }
        }

        return true;
    }

    private static void ValidateSupportedSchema(
        JsonNode schema,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (!visitedSchemas.Add(schema))
        {
            return;
        }

        if (schema is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out _))
        {
            return;
        }

        if (schema is not JsonObject schemaObject)
        {
            throw new InvalidOperationException(
                $"Expected an object or boolean JSON schema at '{location}'.");
        }

        foreach (var (keyword, keywordValue) in schemaObject)
        {
            ValidateKnownKeyword(keyword, location);
            ValidateKeywordValue(keyword, keywordValue, location);
            ValidateNestedSchemas(
                keyword,
                keywordValue,
                rootSchema,
                location,
                visitedSchemas);
        }
    }

    private static void ValidateKeywordValue(
        string keyword,
        JsonNode? keywordValue,
        string location)
    {
        switch (keyword)
        {
            case "$schema":
                ValidateDialect(keywordValue, location);
                break;
            case "type":
                ValidateTypeKeyword(keywordValue, location);
                break;
            case "required":
                ValidateUniqueStringArray(keyword, keywordValue, location, allowEmpty: true);
                break;
            case "enum":
                ValidateEnum(keywordValue, location);
                break;
            case "minItems":
            case "maxItems":
                ValidateNonNegativeInteger(keyword, keywordValue, location);
                break;
            case "$comment":
            case "description":
            case "format":
            case "title":
                ValidateString(keyword, keywordValue, location);
                break;
            case "deprecated":
            case "readOnly":
            case "writeOnly":
                ValidateBoolean(keyword, keywordValue, location);
                break;
            case "examples" when keywordValue is not JsonArray:
                throw InvalidKeywordValue(keyword, location, "an array");
        }
    }

    private static void ValidateDialect(JsonNode? keywordValue, string location)
    {
        if (keywordValue is not JsonValue dialectNode
            || !dialectNode.TryGetValue<string>(out var dialect))
        {
            throw InvalidKeywordValue("$schema", location, "the JSON Schema 2020-12 URI");
        }

        var normalized = dialect.EndsWith('#') ? dialect[..^1] : dialect;
        if (!string.Equals(normalized, Draft202012SchemaUri, StringComparison.Ordinal))
        {
            throw InvalidKeywordValue("$schema", location, "the JSON Schema 2020-12 URI");
        }
    }

    private static void ValidateTypeKeyword(JsonNode? keywordValue, string location)
    {
        if (keywordValue is JsonValue singleType
            && singleType.TryGetValue<string>(out var type))
        {
            ValidateJsonTypeName(type, location);
            return;
        }

        if (keywordValue is not JsonArray typeArray || typeArray.Count == 0)
        {
            throw InvalidKeywordValue("type", location, "a supported type name or non-empty array of unique type names");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeNode in typeArray)
        {
            if (typeNode is not JsonValue typeValue
                || !typeValue.TryGetValue<string>(out var arrayType))
            {
                throw InvalidKeywordValue("type", location, "a supported type name or non-empty array of unique type names");
            }

            ValidateJsonTypeName(arrayType, location);
            if (!names.Add(arrayType))
            {
                throw InvalidKeywordValue("type", location, "an array of unique type names");
            }
        }
    }

    private static void ValidateJsonTypeName(string type, string location)
    {
        if (!_supportedJsonTypes.Contains(type))
        {
            throw InvalidKeywordValue("type", location, "a supported JSON Schema type name");
        }
    }

    private static void ValidateUniqueStringArray(
        string keyword,
        JsonNode? keywordValue,
        string location,
        bool allowEmpty)
    {
        if (keywordValue is not JsonArray values || (!allowEmpty && values.Count == 0))
        {
            throw InvalidKeywordValue(keyword, location, "an array of unique strings");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var valueNode in values)
        {
            if (valueNode is not JsonValue stringValue
                || !stringValue.TryGetValue<string>(out var value)
                || !unique.Add(value))
            {
                throw InvalidKeywordValue(keyword, location, "an array of unique strings");
            }
        }
    }

    private static void ValidateEnum(JsonNode? keywordValue, string location)
    {
        if (keywordValue is not JsonArray values || values.Count == 0)
        {
            throw InvalidKeywordValue("enum", location, "a non-empty array of unique values");
        }

        for (var left = 0; left < values.Count; left++)
        {
            for (var right = left + 1; right < values.Count; right++)
            {
                if (JsonNode.DeepEquals(values[left], values[right]))
                {
                    throw InvalidKeywordValue("enum", location, "a non-empty array of unique values");
                }
            }
        }
    }

    private static void ValidateNonNegativeInteger(
        string keyword,
        JsonNode? keywordValue,
        string location)
    {
        if (keywordValue is not JsonValue integerValue
            || !integerValue.TryGetValue<int>(out var value)
            || value < 0)
        {
            throw InvalidKeywordValue(keyword, location, "a non-negative 32-bit integer");
        }
    }

    private static void ValidateString(string keyword, JsonNode? keywordValue, string location)
    {
        if (keywordValue is not JsonValue stringValue
            || !stringValue.TryGetValue<string>(out _))
        {
            throw InvalidKeywordValue(keyword, location, "a string");
        }
    }

    private static void ValidateBoolean(string keyword, JsonNode? keywordValue, string location)
    {
        if (keywordValue is not JsonValue booleanValue
            || !booleanValue.TryGetValue<bool>(out _))
        {
            throw InvalidKeywordValue(keyword, location, "a boolean");
        }
    }

    private static InvalidOperationException InvalidKeywordValue(
        string keyword,
        string location,
        string expected) =>
        new($"JSON-Schema keyword '{keyword}' at '{location}' must be {expected}.");

    private static void ValidateKnownKeyword(string keyword, string location)
    {
        if (_supportedValidationKeywords.Contains(keyword)
            || _supportedSchemaStructureKeywords.Contains(keyword)
            || _annotationOnlyKeywords.Contains(keyword))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported JSON-Schema keyword '{keyword}' at '{location}'. " +
            "Add explicit evaluation support before relying on it in wire-contract validation.");
    }

    private static void ValidateNestedSchemas(
        string keyword,
        JsonNode? keywordValue,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (keyword is "$defs" or "properties")
        {
            ValidateSchemaMap(keyword, keywordValue, rootSchema, location, visitedSchemas);
        }
        else if (keyword is "allOf" or "anyOf" or "oneOf")
        {
            ValidateSchemaArray(keyword, keywordValue, rootSchema, location, visitedSchemas);
        }
        else if (keyword is "additionalProperties" or "items" or "not")
        {
            ValidateChildSchema(keyword, keywordValue, rootSchema, location, visitedSchemas);
        }
        else if (keyword == "$ref")
        {
            ValidateReference(keywordValue, rootSchema, location, visitedSchemas);
        }
    }

    private static void ValidateSchemaMap(
        string keyword,
        JsonNode? keywordValue,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (keywordValue is not JsonObject schemaMap)
        {
            throw new InvalidOperationException(
                $"JSON-Schema keyword '{keyword}' at '{location}' must be an object.");
        }

        foreach (var (name, childSchema) in schemaMap)
        {
            if (childSchema is null)
            {
                throw new InvalidOperationException(
                    $"JSON-Schema entry '{name}' under '{location}/{keyword}' must not be null.");
            }

            ValidateSupportedSchema(
                childSchema,
                rootSchema,
                $"{location}/{keyword}/{EscapeJsonPointerSegment(name)}",
                visitedSchemas);
        }
    }

    private static void ValidateSchemaArray(
        string keyword,
        JsonNode? keywordValue,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (keywordValue is not JsonArray schemaArray || schemaArray.Count == 0)
        {
            throw new InvalidOperationException(
                $"JSON-Schema keyword '{keyword}' at '{location}' must be a non-empty array.");
        }

        for (var index = 0; index < schemaArray.Count; index++)
        {
            var childSchema = schemaArray[index]
                ?? throw new InvalidOperationException(
                    $"JSON-Schema entry '{location}/{keyword}/{index}' must not be null.");
            ValidateSupportedSchema(
                childSchema,
                rootSchema,
                $"{location}/{keyword}/{index}",
                visitedSchemas);
        }
    }

    private static void ValidateChildSchema(
        string keyword,
        JsonNode? keywordValue,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (keywordValue is null)
        {
            throw new InvalidOperationException(
                $"JSON-Schema keyword '{keyword}' at '{location}' must not be null.");
        }

        ValidateSupportedSchema(
            keywordValue,
            rootSchema,
            $"{location}/{keyword}",
            visitedSchemas);
    }

    private static void ValidateReference(
        JsonNode? keywordValue,
        JsonNode rootSchema,
        string location,
        HashSet<JsonNode> visitedSchemas)
    {
        if (keywordValue is not JsonValue referenceNode
            || !referenceNode.TryGetValue<string>(out var reference))
        {
            throw new InvalidOperationException(
                $"JSON-Schema keyword '$ref' at '{location}' must be a string.");
        }

        var referencedSchema = ResolveReference(rootSchema, reference);
        ValidateSupportedSchema(referencedSchema, rootSchema, reference, visitedSchemas);
    }

    private static string EscapeJsonPointerSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static JsonNode ResolveReference(JsonNode rootSchema, string reference)
    {
        if (reference == "#")
        {
            return rootSchema;
        }

        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported non-local JSON-Schema reference '{reference}'.");
        }

        JsonNode? current = rootSchema;
        foreach (var encodedSegment in reference[2..].Split('/'))
        {
            var segment = encodedSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current switch
            {
                JsonObject currentObject when currentObject.TryGetPropertyValue(segment, out var child) => child,
                JsonArray currentArray when int.TryParse(segment, out var index)
                    && index >= 0
                    && index < currentArray.Count => currentArray[index],
                _ => null,
            };

            if (current is null)
            {
                throw new InvalidOperationException(
                    $"JSON-Schema reference '{reference}' does not resolve against the advertised root schema.");
            }
        }

        return current;
    }

    private static bool MatchesJsonType(JsonElement value, JsonNode typeNode)
    {
        var expectedTypes = typeNode is JsonArray typeArray
            ? typeArray.Select(static node => node?.GetValue<string>()).Where(static type => type is not null)
            : [typeNode.GetValue<string>()];

        return expectedTypes.Any(type => type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => IsMathematicalInteger(value),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => throw new InvalidOperationException($"Unsupported JSON-Schema type '{type}'."),
        });
    }

    private static bool IsMathematicalInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var number = value.GetRawText().AsSpan();
        if (number[0] == '-')
        {
            number = number[1..];
        }

        var exponentSeparator = number.IndexOfAny('e', 'E');
        var significand = exponentSeparator >= 0 ? number[..exponentSeparator] : number;
        var exponent = exponentSeparator >= 0
            ? BigInteger.Parse(number[(exponentSeparator + 1)..])
            : BigInteger.Zero;
        var decimalSeparator = significand.IndexOf('.');
        var fractionalDigits = decimalSeparator >= 0
            ? significand.Length - decimalSeparator - 1
            : 0;
        var effectiveScale = new BigInteger(fractionalDigits) - exponent;
        if (effectiveScale <= BigInteger.Zero)
        {
            return true;
        }

        var digits = decimalSeparator >= 0
            ? string.Concat(significand[..decimalSeparator], significand[(decimalSeparator + 1)..])
            : significand.ToString();
        if (effectiveScale > digits.Length)
        {
            return digits.All(static digit => digit == '0');
        }

        var scale = (int)effectiveScale;
        return digits.AsSpan(digits.Length - scale).IndexOfAnyExcept('0') < 0;
    }

    private sealed class EvaluationContext
    {
        private readonly Dictionary<JsonNode, HashSet<string>> _activeReferences =
            new(ReferenceEqualityComparer.Instance);

        public bool TryEnterReference(JsonNode schema, string valueLocation)
        {
            if (!_activeReferences.TryGetValue(schema, out var locations))
            {
                locations = new HashSet<string>(StringComparer.Ordinal);
                _activeReferences.Add(schema, locations);
            }

            return locations.Add(valueLocation);
        }

        public void ExitReference(JsonNode schema, string valueLocation)
        {
            var locations = _activeReferences[schema];
            locations.Remove(valueLocation);
            if (locations.Count == 0)
            {
                _activeReferences.Remove(schema);
            }
        }
    }
}
