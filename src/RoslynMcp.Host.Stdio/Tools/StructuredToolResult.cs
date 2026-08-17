using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Creates the dual-channel response for tools that advertise an output schema.
/// A typed payload is the single source for both the legacy text block and
/// <see cref="CallToolResult.StructuredContent"/>, so the SDK never attempts to
/// structure an already-serialized CLR string.
/// </summary>
internal static class StructuredToolResult
{
    public static CallToolResult Create<TPayload>(TPayload payload)
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload is string or JsonElement or JsonDocument or JsonNode)
        {
            throw new ArgumentException(
                "Structured tool results require a typed DTO, not pre-serialized JSON.",
                nameof(payload));
        }

        var structured = JsonSerializer.SerializeToElement(payload, JsonDefaults.Indented);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = structured.GetRawText() }],
            StructuredContent = structured,
        };
    }
}
