using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests;

internal static class CallToolResultTestExtensions
{
    public static string TextPayload(this CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Content is [TextContentBlock { Text: { } text }, ..]
            ? text
            : throw new InvalidOperationException(
                "Expected the tool result to contain a leading text content block.");
    }
}
