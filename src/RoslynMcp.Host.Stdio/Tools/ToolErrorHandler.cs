using Microsoft.Extensions.Logging;

namespace RoslynMcp.Host.Stdio.Tools;

internal static class ToolErrorHandler
{
    /// <summary>
    /// Wraps a tool action with structured error handling. Rethrows exceptions with clean,
    /// actionable messages so the MCP SDK sets isError=true on the tool result.
    /// Error messages include retry guidance to help agents self-correct.
    /// </summary>
    public static async Task<string> ExecuteAsync(Func<Task<string>> action, ILogger? auditLogger = null, string? toolName = null)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            auditLogger?.LogInformation("Tool {ToolName} completed successfully", toolName ?? "unknown");
            return result;
        }
        catch (OperationCanceledException)
        {
            auditLogger?.LogWarning("Tool {ToolName} was cancelled", toolName ?? "unknown");
            throw; // Let MCP SDK handle cancellation
        }
        catch (FileNotFoundException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} failed: file not found", toolName ?? "unknown");
            throw new McpToolException(
                $"File not found: {ex.Message}. Verify the file path is absolute and the file exists on disk. " +
                $"If the workspace was recently reloaded, the file may have been removed.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} failed: directory not found", toolName ?? "unknown");
            throw new McpToolException(
                $"Directory not found: {ex.Message}. Verify the directory path is absolute and exists on disk.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} failed: not found", toolName ?? "unknown");
            throw new McpToolException(
                $"Not found: {ex.Message}. Ensure the workspace is loaded (workspace_load) and the identifier is correct.", ex);
        }
        catch (ArgumentException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} failed: invalid argument", toolName ?? "unknown");
            throw new McpToolException(
                $"Invalid argument: {ex.Message}. Check parameter types and values match the tool schema.", ex);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} rate-limited", toolName ?? "unknown");
            throw new McpToolException(
                $"{ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} failed: invalid operation", toolName ?? "unknown");
            throw new McpToolException(
                $"Invalid operation: {ex.Message}. The workspace may need to be reloaded (workspace_reload) if the state is stale.", ex);
        }
        catch (TimeoutException ex)
        {
            auditLogger?.LogWarning(ex, "Tool {ToolName} timed out", toolName ?? "unknown");
            throw new McpToolException(
                $"Timed out: {ex.Message}. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or " +
                $"ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS.", ex);
        }
        catch (McpToolException)
        {
            throw; // Already wrapped
        }
        catch (Exception ex)
        {
            auditLogger?.LogError(ex, "Tool {ToolName} failed with unexpected error", toolName ?? "unknown");
            throw new McpToolException(
                $"Internal error: {ex.Message}. If this persists, try reloading the workspace (workspace_reload).", ex);
        }
    }
}

/// <summary>
/// Exception type for tool errors. The MCP SDK will catch this and set isError=true on the result.
/// </summary>
public sealed class McpToolException(string message, Exception? inner = null) : Exception(message, inner);
