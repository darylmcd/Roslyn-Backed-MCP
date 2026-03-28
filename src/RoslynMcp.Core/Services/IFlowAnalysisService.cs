using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides data flow and control flow analysis for code regions using the Roslyn semantic model.
/// </summary>
public interface IFlowAnalysisService
{
    /// <summary>
    /// Analyzes how variables flow through the statement range defined by <paramref name="startLine"/>
    /// to <paramref name="endLine"/>: which are read, written, captured, always assigned, etc.
    /// </summary>
    Task<DataFlowAnalysisDto> AnalyzeDataFlowAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct);

    /// <summary>
    /// Analyzes control flow through the statement range defined by <paramref name="startLine"/>
    /// to <paramref name="endLine"/>: entry/exit points, reachability, and return statements.
    /// </summary>
    Task<ControlFlowAnalysisDto> AnalyzeControlFlowAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct);
}
