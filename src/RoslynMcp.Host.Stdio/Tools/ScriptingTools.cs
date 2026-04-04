using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ScriptingTools
{
    [McpServerTool(Name = "evaluate_csharp", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Evaluate a C# expression or script interactively using the Roslyn Scripting API. Returns the result value and type. The server enforces a script timeout (default 10 seconds, override with env ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS); infinite loops are cancelled server-side. The MCP client may also enforce its own session timeout (e.g. error -32001) independently of the server.")]
    public static Task<string> EvaluateCSharp(
        IScriptingService scriptingService,
        [Description("The C# code to evaluate (expression, statement, or multi-line script)")] string code,
        [Description("Optional: additional namespace imports, e.g. [\"System.IO\", \"System.Net.Http\"]")] string[]? imports = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(async () =>
        {
            var result = await scriptingService.EvaluateAsync(code, imports, ct);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        });
    }
}
