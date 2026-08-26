using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SnippetAnalysisTools
{
    /// <remarks>
    /// The <c>kind</c> parameter selects the wrapper the snippet is compiled inside:
    /// <c>expression</c> (single expression), <c>statements</c> (void method body — use this for
    /// code without a return value), <c>returnExpression</c> (<c>object?</c>-returning method
    /// body — use this when you need <c>return &lt;value&gt;;</c>), <c>members</c> (class members),
    /// and <c>program</c> (full compilation unit, the default). Diagnostic line <i>and</i> column
    /// numbers are 1-based and relative to the user-supplied code (wrapper offsets are subtracted
    /// server-side, UX-001 + FLAG-C), so positions always point inside the original snippet.
    /// </remarks>
    [McpServerTool(Name = "analyze_snippet", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Analyze a C# code snippet in an ephemeral workspace without loading a solution."),
     Description("Analyze a C# snippet for compilation errors and declared symbols in an ephemeral workspace — no solution load. Use for fragments, diffs, or pasted code; `kind` selects the wrapper.")]
    public static async Task<string> AnalyzeSnippet(
        ISnippetAnalysisService snippetAnalysisService,
        [Description("The C# code to analyze")] string code,
        [Description("Optional: additional using directives. Pass as a native JSON array, not a JSON-encoded string. Example: [\"System.IO\", \"System.Net.Http\"].")] string[]? usings = null,
        [Description("The snippet kind: 'expression', 'statements', 'returnExpression', 'members', or 'program' (default)")] string kind = "program",
        CancellationToken ct = default)
    {
        var result = await snippetAnalysisService.AnalyzeAsync(code, usings, kind, ct);
        return JsonSerializer.Serialize(result, JsonDefaults.Indented);
    }
}
