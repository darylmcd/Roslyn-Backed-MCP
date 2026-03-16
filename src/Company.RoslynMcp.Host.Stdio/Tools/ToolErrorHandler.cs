namespace Company.RoslynMcp.Host.Stdio.Tools;

internal static class ToolErrorHandler
{
    /// <summary>
    /// Wraps a tool action with structured error handling. Rethrows exceptions with clean messages
    /// so the MCP SDK sets isError=true on the tool result.
    /// </summary>
    public static async Task<string> ExecuteAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Let MCP SDK handle cancellation
        }
        catch (KeyNotFoundException ex)
        {
            throw new McpToolException($"Not found: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new McpToolException($"Invalid argument: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpToolException($"Invalid operation: {ex.Message}", ex);
        }
        catch (McpToolException)
        {
            throw; // Already wrapped
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Internal error: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Exception type for tool errors. The MCP SDK will catch this and set isError=true on the result.
/// </summary>
public sealed class McpToolException(string message, Exception? inner = null) : Exception(message, inner);
