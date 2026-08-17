using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcp.Tests;

/// <summary>
/// Evaluates the JSON-Schema keywords emitted by <see cref="System.Text.Json.Schema.JsonSchemaExporter"/>
/// for this test suite. Local references are resolved against the advertised root schema so a
/// schema change cannot false-green by falling through an unknown reference.
/// </summary>
internal static class GeneratedJsonSchemaMatcher
{
    public static bool Matches(JsonElement value, JsonNode schema) =>
        Matches(value, schema, schema);

    private static bool Matches(JsonElement value, JsonNode schema, JsonNode rootSchema)
    {
        if (schema is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out var allowed))
        {
            return allowed;
        }

        if (schema is not JsonObject schemaObject)
        {
            throw new InvalidOperationException("Expected an object or boolean JSON schema.");
        }

        if (schemaObject["$ref"] is JsonValue referenceNode
            && referenceNode.TryGetValue<string>(out var reference))
        {
            return Matches(value, ResolveReference(rootSchema, reference), rootSchema);
        }

        if (schemaObject["allOf"] is JsonArray allOf
            && !allOf.All(candidate => candidate is not null && Matches(value, candidate, rootSchema)))
        {
            return false;
        }

        if (schemaObject["oneOf"] is JsonArray oneOf
            && oneOf.Count(candidate => candidate is not null && Matches(value, candidate, rootSchema)) != 1)
        {
            return false;
        }

        if (schemaObject["anyOf"] is JsonArray anyOf
            && !anyOf.Any(candidate => candidate is not null && Matches(value, candidate, rootSchema)))
        {
            return false;
        }

        if (schemaObject["not"] is { } notSchema && Matches(value, notSchema, rootSchema))
        {
            return false;
        }

        if (schemaObject["type"] is { } typeNode && !MatchesJsonType(value, typeNode))
        {
            return false;
        }

        var valueNode = JsonNode.Parse(value.GetRawText());
        if (schemaObject["const"] is { } constNode && !JsonNode.DeepEquals(valueNode, constNode))
        {
            return false;
        }

        if (schemaObject["enum"] is JsonArray enumValues
            && !enumValues.Any(candidate => JsonNode.DeepEquals(valueNode, candidate)))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => MatchesObject(value, schemaObject, rootSchema),
            JsonValueKind.Array => MatchesArray(value, schemaObject, rootSchema),
            _ => true,
        };
    }

    private static bool MatchesObject(
        JsonElement value,
        JsonObject schema,
        JsonNode rootSchema)
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
                if (!Matches(property.Value, propertySchema, rootSchema))
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
                && !Matches(property.Value, additionalPropertySchema, rootSchema))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesArray(
        JsonElement value,
        JsonObject schema,
        JsonNode rootSchema)
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
            foreach (var item in value.EnumerateArray())
            {
                if (!Matches(item, itemSchema, rootSchema))
                {
                    return false;
                }
            }
        }

        return true;
    }

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
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => throw new InvalidOperationException($"Unsupported JSON-Schema type '{type}'."),
        });
    }
}
