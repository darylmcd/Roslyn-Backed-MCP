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
    /// <remarks>
    /// The server default budget is ten seconds. <c>timeoutSeconds</c> overrides it per call;
    /// <c>ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS</c> sets the server default. After the configured budget
    /// plus <c>ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS</c>, the route returns a timeout even when
    /// Roslyn code ignores cancellation. Client session timeouts remain independent.
    /// </remarks>
    [McpServerTool(Name = "evaluate_csharp", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("scripting", "stable", true, false,
        "Evaluate a C# expression or script interactively via the Roslyn Scripting API. Emits MCP progress and heartbeat logs during long compile/run so clients are not stuck on a static label."),
     Description("Evaluate a C# expression or multi-line script and return its value and type. Use timeoutSeconds for a per-call budget; progress reports cover long compile and execution work.")]
    public static async Task<string> EvaluateCSharp(
        IScriptingService scriptingService,
        [Description("The C# code to evaluate (expression, statement, or multi-line script)")] string code,
        [Description("Optional: additional namespace imports. Pass as a native JSON array, not a JSON-encoded string. Example: [\"System.IO\", \"System.Net.Http\"].")] string[]? imports = null,
        [Description("Optional: per-call timeout in seconds (UX-002). Overrides ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS for this single invocation. Must be > 0 and fit the server timer range after watchdog grace; null falls back to the configured default.")] int? timeoutSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
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
    }
}
