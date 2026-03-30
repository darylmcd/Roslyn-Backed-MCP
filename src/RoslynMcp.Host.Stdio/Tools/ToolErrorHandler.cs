using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Host.Stdio.Tools;

internal static class ToolErrorHandler
{
    private static readonly Dictionary<Type, Func<Exception, string, ErrorInfo>> ErrorHandlers = new()
    {
        [typeof(FileNotFoundException)] = (ex, _) => new("FileNotFound",
            $"File not found: {ex.Message}. Verify the file path is absolute and the file exists on disk. " +
            "If the workspace was recently reloaded, the file may have been removed."),
        [typeof(DirectoryNotFoundException)] = (ex, _) => new("DirectoryNotFound",
            $"Directory not found: {ex.Message}. Verify the directory path is absolute and exists on disk."),
        [typeof(KeyNotFoundException)] = (ex, _) => new("NotFound",
            $"Not found: {ex.Message}. Ensure the workspace is loaded (workspace_load) and the identifier is correct."),
        [typeof(ArgumentException)] = (ex, _) => new("InvalidArgument",
            $"Invalid argument: {ex.Message}. Check parameter types and values match the tool schema."),
        [typeof(TimeoutException)] = (ex, _) => new("Timeout",
            $"Timed out: {ex.Message}. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or " +
            "ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS."),
        [typeof(InvalidOperationException)] = (ex, _) => ex.Message.Contains("Rate limit")
            ? new("RateLimited", ex.Message)
            : new("InvalidOperation",
                $"Invalid operation: {ex.Message}. The workspace may need to be reloaded (workspace_reload) if the state is stale."),
    };

    /// <summary>
    /// Wraps a tool action with structured error handling. Returns errors as structured JSON content
    /// so MCP clients always receive actionable error details, regardless of SDK exception handling.
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
            throw; // Let MCP SDK handle cancellation natively
        }
        catch (Exception ex)
        {
            var info = ClassifyError(ex, toolName ?? "unknown");
            auditLogger?.Log(
                info.Category == "InternalError" ? LogLevel.Error : LogLevel.Warning,
                ex, "Tool {ToolName} failed: {ErrorCategory}", toolName ?? "unknown", info.Category);

            return FormatErrorResponse(info, toolName ?? "unknown", ex);
        }
    }

    private static ErrorInfo ClassifyError(Exception ex, string toolName)
    {
        // Walk the handler dictionary for exact or assignable type match
        foreach (var (type, handler) in ErrorHandlers)
        {
            if (type.IsAssignableFrom(ex.GetType()))
                return handler(ex, toolName);
        }

        // Fallback: unexpected error — include inner exception chain for diagnosis
        var innerMessages = GetInnerExceptionChain(ex);
        var detail = string.IsNullOrEmpty(innerMessages)
            ? $"{ex.GetType().Name}: {ex.Message}"
            : $"{ex.GetType().Name}: {ex.Message} --> {innerMessages}";

        return new("InternalError",
            $"Internal error in {toolName}: {detail}. " +
            "If this persists, try reloading the workspace (workspace_reload).");
    }

    private static string GetInnerExceptionChain(Exception ex)
    {
        var parts = new List<string>();
        var inner = ex.InnerException;
        while (inner is not null && parts.Count < 3)
        {
            parts.Add($"{inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
        }
        return string.Join(" --> ", parts);
    }

    private static string GetAbbreviatedStackTrace(Exception ex)
    {
        if (ex.StackTrace is null) return string.Empty;
        var frames = ex.StackTrace
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .ToArray();
        return string.Join("\n", frames);
    }

    private static string FormatErrorResponse(ErrorInfo info, string toolName, Exception ex)
    {
        if (info.Category == "InternalError")
        {
            var error = new
            {
                error = true,
                category = info.Category,
                tool = toolName,
                message = info.Message,
                exceptionType = ex.GetType().Name,
                stackTrace = GetAbbreviatedStackTrace(ex),
            };
            return JsonSerializer.Serialize(error, JsonDefaults.Indented);
        }
        else
        {
            var error = new
            {
                error = true,
                category = info.Category,
                tool = toolName,
                message = info.Message,
                exceptionType = ex.GetType().Name,
            };
            return JsonSerializer.Serialize(error, JsonDefaults.Indented);
        }
    }

    private readonly record struct ErrorInfo(string Category, string Message);
}

/// <summary>
/// Exception type for tool errors. Can be used by resource handlers and other non-tool contexts
/// where structured JSON error responses are not applicable.
/// </summary>
public sealed class McpToolException(string message, Exception? inner = null) : Exception(message, inner);
