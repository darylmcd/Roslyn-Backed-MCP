using System.Text.Json;

namespace Company.RoslynMcp.Host.Stdio.Tools;

internal static class ToolErrorHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Wraps a tool action with structured error handling. Returns JSON error responses
    /// instead of propagating raw exceptions through the MCP transport.
    /// </summary>
    public static async Task<string> ExecuteAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Operation was cancelled." }, JsonOptions);
        }
        catch (KeyNotFoundException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, errorType = "NotFound" }, JsonOptions);
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, errorType = "InvalidArgument" }, JsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, errorType = "InvalidOperation" }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, errorType = "InternalError" }, JsonOptions);
        }
    }
}
