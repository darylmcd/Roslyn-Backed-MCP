using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The structured-content / <c>_meta</c> projection layer extracted from
/// <see cref="StructuredCallToolFilter"/>. Owns injection of the gate-metrics snapshot as a
/// top-level <c>_meta</c> property on the response's first text block and the dual-channel
/// <see cref="CallToolResult.StructuredContent"/> mirror for tools with a registered output schema.
/// Holds no error-classification, dispatch, or allowlist concerns; those stay in the filter,
/// <see cref="StructuredCallElicitationCoordinator"/>, and <see cref="ElicitationAllowlistPolicy"/>.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> keeps thin delegates that forward to these overloads, so
/// its historical static call surface (consumed by the existing filter/content test suites) is
/// preserved byte-for-byte.
/// </para>
/// </summary>
internal static class StructuredCallContentProjector
{
    /// <summary>
    /// Injects the gate-metrics snapshot as a top-level <c>_meta</c> property on the first
    /// text content block when the tool's JSON response is object-rooted. Arrays,
    /// primitives, and non-text content are returned unchanged — preserving the
    /// historical contract that e.g. <c>source_generated_documents</c>'s bare-array
    /// response shape remains stable across the filter migration. Exposed <c>internal</c>
    /// so tests can assert meta-injection behavior directly.
    ///
    /// <para><b>tool-output-schema-infrastructure (MCP 2025-06-18 § Tools / Structured Content):</b></para>
    /// <para>
    /// When the tool has a registered <c>outputSchema</c> via
    /// <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/>, this method ALSO populates
    /// <see cref="CallToolResult.StructuredContent"/> with the same payload (sans <c>_meta</c>) so
    /// the structured-content channel is non-empty. Per spec, when <c>structuredContent</c> is
    /// emitted the server MUST also emit a serialized JSON copy in the <c>content[].text</c>
    /// channel — both channels coexist; <c>_meta</c> lives only in the text channel so clients
    /// never see two observability blobs (defense against the dedupe risk noted in the
    /// initiative plan).
    /// </para>
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName) =>
        InjectMetaIntoContent(result, toolName, ToolOutputSchemaIndex.GetSchema);

    /// <summary>
    /// Test seam: same as <see cref="InjectMetaIntoContent(CallToolResult, string)"/> but lets
    /// the caller supply a custom schema resolver so dual-channel behavior can be exercised
    /// without needing a live <c>[McpToolMetadata(outputSchemaTypeRef:)]</c> opt-in. The
    /// production path always uses the static <see cref="ToolOutputSchemaIndex"/>.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(
        CallToolResult result, string toolName, Func<string, JsonNode?> schemaResolver)
    {
        if (result.Content is null || result.Content.Count == 0)
        {
            return result;
        }

        if (result.Content[0] is not TextContentBlock text || string.IsNullOrEmpty(text.Text))
        {
            return result;
        }

        // Parse once so both the meta-injection path and the structuredContent path can share
        // a single JsonNode tree. Non-JSON / array-rooted responses bail out early as before.
        JsonNode? parsedRoot = null;
        try
        {
            parsedRoot = JsonNode.Parse(text.Text);
        }
        catch (JsonException)
        {
            // Fall through to the original best-effort path; non-JSON responses pass through.
        }

        var schema = schemaResolver(toolName);
        // structuredContent is only emitted when (a) the tool opted in via OutputSchemaTypeRef
        // and (b) the response is an object-rooted JSON document we can mirror. Arrays, scalars,
        // and non-JSON responses leave structuredContent absent — matching the spec's "MAY"
        // semantics rather than fabricating a structured shape that doesn't match the schema.
        // CallToolResult.StructuredContent is a JsonElement? — convert from JsonNode via the
        // round-trip text. The body is small (already serialized once for the text channel)
        // so the extra parse is bounded; deep-clone to detach from the parsed tree.
        JsonElement? structuredFromBody = null;
        if (schema is not null && parsedRoot is JsonObject bodyObj)
        {
            structuredFromBody = JsonDocument.Parse(bodyObj.ToJsonString()).RootElement.Clone();
        }

        var injected = ToolErrorHandler.InjectMetaIfPossible(text.Text, toolName);
        var textChanged = !(ReferenceEquals(injected, text.Text) || injected == text.Text);

        if (!textChanged && structuredFromBody is null)
        {
            // Nothing to change — skip the allocation so array-rooted and non-JSON
            // responses pass through byte-for-byte identical.
            return result;
        }

        var newContent = new List<ContentBlock>(result.Content.Count)
        {
            new TextContentBlock { Text = textChanged ? injected : text.Text }
        };
        for (var i = 1; i < result.Content.Count; i++)
        {
            newContent.Add(result.Content[i]);
        }

        return new CallToolResult
        {
            IsError = result.IsError,
            Content = newContent,
            // Preserve any pre-existing StructuredContent (a tool may have set it directly);
            // otherwise emit the schema-mirrored body when the tool has opted in.
            StructuredContent = result.StructuredContent ?? structuredFromBody,
        };
    }
}
