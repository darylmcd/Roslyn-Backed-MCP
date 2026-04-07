using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class FlowAnalysisTools
{
    [McpServerTool(Name = "analyze_data_flow", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Analyze how variables flow through a code region: which are read, written, captured by lambdas, always assigned, etc. Use to understand data dependencies before refactoring.")]
    public static Task<string> AnalyzeDataFlow(
        IWorkspaceExecutionGate gate,
        IFlowAnalysisService flowAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line of the code region to analyze")] int startLine,
        [Description("1-based end line of the code region to analyze")] int endLine,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("analyze_data_flow", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await flowAnalysisService.AnalyzeDataFlowAsync(workspaceId, filePath, startLine, endLine, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "analyze_control_flow", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Analyze control flow through a code region: entry/exit points, reachability, and return statements. EndPointIsReachable follows Roslyn semantics (whether execution can fall through the end of the region without return/throw), not whether the method can complete; see Warning when returns exist but EndPointIsReachable is false.")]
    public static Task<string> AnalyzeControlFlow(
        IWorkspaceExecutionGate gate,
        IFlowAnalysisService flowAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line of the code region to analyze")] int startLine,
        [Description("1-based end line of the code region to analyze")] int endLine,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("analyze_control_flow", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await flowAnalysisService.AnalyzeControlFlowAsync(workspaceId, filePath, startLine, endLine, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
