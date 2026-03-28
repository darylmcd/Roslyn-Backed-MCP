using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ScriptingTools
{
    [McpServerTool(Name = "evaluate_csharp", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Evaluate a C# expression or script interactively using the Roslyn Scripting API. Returns the result value and type. Use for quick expression testing, prototyping, and validating logic without creating files or running builds.")]
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
