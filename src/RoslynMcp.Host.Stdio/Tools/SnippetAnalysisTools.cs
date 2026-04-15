using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SnippetAnalysisTools
{
    [McpServerTool(Name = "analyze_snippet", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Analyze a C# code snippet in an ephemeral workspace without loading a solution."),
     Description("Analyze a C# code snippet for compilation errors and declared symbols without loading a full solution. Uses an ephemeral workspace for quick validation of code fragments, diffs, or pasted code. Kinds: 'expression' (single expression), 'statements' (void method body — use this for code without a return value), 'returnExpression' (object?-returning method body — use this when you need 'return <value>;'), 'members' (class members), 'program' (full compilation unit, default). Diagnostic line *and* column numbers are 1-based and relative to the user-supplied code (wrapper offsets are subtracted server-side, UX-001 + FLAG-C), so positions always point inside the original snippet.")]
    public static Task<string> AnalyzeSnippet(
        ISnippetAnalysisService snippetAnalysisService,
        [Description("The C# code to analyze")] string code,
        [Description("Optional: additional using directives as an array, e.g. [\"System.IO\", \"System.Net.Http\"]")] string[]? usings = null,
        [Description("The snippet kind: 'expression', 'statements', 'returnExpression', 'members', or 'program' (default)")] string kind = "program",
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("analyze_snippet", async () =>
        {
            var result = await snippetAnalysisService.AnalyzeAsync(code, usings, kind, ct);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        });
    }
}
