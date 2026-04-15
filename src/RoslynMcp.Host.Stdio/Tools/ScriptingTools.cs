using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ScriptingTools
{
    [McpServerTool(Name = "evaluate_csharp", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("scripting", "stable", true, false,
        "Evaluate a C# expression or script interactively via the Roslyn Scripting API. Emits MCP progress and heartbeat logs during long compile/run so clients are not stuck on a static label."),
     Description("Evaluate a C# expression or script interactively using the Roslyn Scripting API. Returns the result value and type. The server enforces a script budget (default 10 seconds, override per-call with timeoutSeconds or env ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS — UX-002). After budget + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS (default 10), the tool returns a timeout response even if Roslyn is still running (e.g. tight infinite loops do not honor CancellationToken). A Critical watchdog log may still fire on a timer for observability. MCP progress and heartbeats apply as documented. The MCP client may enforce its own session timeout (e.g. error -32001) independently of the server.")]
    public static Task<string> EvaluateCSharp(
        IScriptingService scriptingService,
        [Description("The C# code to evaluate (expression, statement, or multi-line script)")] string code,
        [Description("Optional: additional namespace imports, e.g. [\"System.IO\", \"System.Net.Http\"]")] string[]? imports = null,
        [Description("Optional: per-call timeout in seconds (UX-002). Overrides ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS for this single invocation. Must be > 0; null falls back to the configured default.")] int? timeoutSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("evaluate_csharp", async () =>
        {
            if (timeoutSeconds is <= 0)
                throw new ArgumentException("timeoutSeconds must be greater than 0 when supplied.", nameof(timeoutSeconds));

            ProgressHelper.Report(progress, 0, 1);
            try
            {
                Action<ScriptEvaluationProgress>? onProgress = null;
                if (progress is not null)
                {
                    onProgress = p =>
                    {
                        var fraction = (float)Math.Min(
                            0.99,
                            p.Elapsed.TotalSeconds / Math.Max(0.001, p.Budget.TotalSeconds));
                        ProgressHelper.Report(progress, fraction, 1);
                    };
                }

                var result = await scriptingService.EvaluateAsync(code, imports, ct, onProgress, timeoutSeconds).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }
            finally
            {
                ProgressHelper.Report(progress, 1, 1);
            }
        });
    }
}
