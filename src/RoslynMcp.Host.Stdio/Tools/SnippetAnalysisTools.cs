using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SnippetAnalysisTools
{
    [McpServerTool(Name = "analyze_snippet", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Analyze a C# code snippet for compilation errors and declared symbols without loading a full solution. Uses an ephemeral workspace for quick validation of code fragments, diffs, or pasted code. For kind 'statements', code is wrapped in a void method — use evaluate_csharp for top-level scripts that return values. Diagnostic line numbers are 1-based and relative to the user-supplied code (the wrapper offset is subtracted server-side, UX-001), so a diagnostic at line 1 always points at the first line of your snippet.")]
    public static Task<string> AnalyzeSnippet(
        ISnippetAnalysisService snippetAnalysisService,
        [Description("The C# code to analyze")] string code,
        [Description("Optional: additional using directives as an array, e.g. [\"System.IO\", \"System.Net.Http\"]")] string[]? usings = null,
        [Description("The snippet kind: 'expression' (single expression), 'statements' (method body), 'members' (class members), or 'program' (full compilation unit, default)")] string kind = "program",
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(async () =>
        {
            var result = await snippetAnalysisService.AnalyzeAsync(code, usings, kind, ct);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        });
    }
}
