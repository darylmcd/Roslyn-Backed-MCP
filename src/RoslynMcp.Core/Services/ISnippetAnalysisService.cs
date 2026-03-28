using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides code analysis for standalone C# snippets using an ephemeral AdhocWorkspace,
/// without requiring a loaded solution.
/// </summary>
public interface ISnippetAnalysisService
{
    /// <summary>
    /// Analyzes a C# code snippet for compilation errors and declared symbols.
    /// </summary>
    /// <param name="code">The C# code to analyze.</param>
    /// <param name="usings">Optional additional using directives.</param>
    /// <param name="kind">The snippet kind: "expression", "statements", "members", or "program" (default).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SnippetAnalysisDto> AnalyzeAsync(string code, string[]? usings, string kind, CancellationToken ct);
}
