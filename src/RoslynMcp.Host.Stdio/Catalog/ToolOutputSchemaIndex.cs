using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// tool-output-schema-infrastructure: cached reflection over every
/// <see cref="McpToolMetadataAttribute"/>-attributed method that declares an
/// <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/>, projecting each tool's
/// declared DTO type into a JSON-Schema document via
/// <see cref="System.Text.Json.Schema.JsonSchemaExporter"/>.
/// <para>
/// The catalog publishes the generated schema per tool on <see cref="SurfaceEntry.OutputSchema"/>
/// so MCP clients (and the dual-channel <c>StructuredCallToolFilter</c>) can know in advance
/// what shape to expect on the <c>structuredContent</c> channel. Reflection runs once at first
/// access; the dictionary is immutable thereafter — schema generation per type is deterministic
/// so the cache is safe across the server's lifetime.
/// </para>
/// <para>
/// Tools without an <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/> are absent from
/// the dictionary and <see cref="GetSchema"/> returns <see langword="null"/>. Registered schemas
/// are projected into the SDK's protocol-tool objects by <see cref="SurfaceRegistrationPolicy"/>.
/// </para>
/// </summary>
internal static class ToolOutputSchemaIndex
{
    /// <summary>
    /// Schema-export options matched to the server's runtime serializer. We do NOT include
    /// reference handling or polymorphic discriminators because tool outputs are flat DTOs by
    /// convention; if a tool ever needs deeper shape, the contract opt-in is to add the
    /// <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/> on a properly-shaped record.
    /// </summary>
    private static readonly JsonSchemaExporterOptions _exportOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
    };

    private static readonly JsonSerializerOptions _schemaSerializerOptions = new(JsonDefaults.Indented)
    {
        TypeInfoResolver = JsonDefaults.Indented.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
    };

    private static readonly Lazy<IReadOnlyDictionary<string, JsonNode>> _index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns the cached JSON-Schema node for <paramref name="toolName"/>, or
    /// <see langword="null"/> when the tool has no declared output schema (the legacy
    /// text-only contract; structuredContent stays absent).
    /// </summary>
    public static JsonNode? GetSchema(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return null;
        return _index.Value.TryGetValue(toolName, out var schema)
            // Return a deep clone so callers cannot mutate the cached node.
            ? schema.DeepClone()
            : null;
    }

    /// <summary>
    /// Returns the cached set of tool names that have an opted-in output schema. Used by tests
    /// to spot the surface count without re-running the schema generation pipeline.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredToolNames => _index.Value.Keys.ToArray();

    /// <summary>
    /// Generates a JSON-Schema node for the given CLR type using the server's runtime
    /// serializer options. Exposed <see langword="internal"/> so the dual-channel filter can
    /// also use it ad-hoc when the tool's response object is built but the cached schema is
    /// keyed off the tool name (cache hit) — both paths produce identical output for the same
    /// type.
    /// </summary>
    internal static JsonNode GenerateSchema(Type type) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(_schemaSerializerOptions, type, _exportOptions);

    private static IReadOnlyDictionary<string, JsonNode> BuildIndex()
    {
        // Anchor on a known tool-host type so we walk the same assembly that MCP discovery uses.
        var assembly = typeof(Tools.ServerTools).Assembly;
        var dict = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr?.Name is null) continue;

                var metadataAttr = method.GetCustomAttribute<McpToolMetadataAttribute>();
                var schemaType = metadataAttr?.OutputSchemaTypeRef;
                if (schemaType is null) continue;

                // An opted-in schema is part of the advertised public contract. Let export
                // failures abort startup instead of silently degrading that tool to text-only
                // output while its metadata still claims structured-content support.
                dict[toolAttr.Name] = GenerateAdvertisedSchema(toolAttr.Name, schemaType);
            }
        }

        return dict;
    }

    private static JsonNode GenerateAdvertisedSchema(string toolName, Type schemaType) =>
        toolName switch
        {
            // Both workspace-list variants have the same outer object shape. In particular,
            // { count: 0, workspaces: [] } satisfies both item schemas because an empty array
            // has no element with which to distinguish summary from verbose. `oneOf` would
            // therefore reject a valid fresh-host response; `anyOf` expresses the real contract.
            "workspace_list" => GenerateObjectVariantSchema(
                "anyOf",
                schemaType,
                typeof(WorkspaceListVerboseDto)),
            "workspace_status" => GenerateObjectVariantSchema(
                "oneOf",
                schemaType,
                typeof(WorkspaceStatusDto)),
            _ => GenerateSchema(schemaType),
        };

    private static JsonNode GenerateObjectVariantSchema(string keyword, params Type[] variants) =>
        new JsonObject
        {
            ["type"] = "object",
            [keyword] = new JsonArray(variants.Select(GenerateSchema).ToArray()),
        };
}
