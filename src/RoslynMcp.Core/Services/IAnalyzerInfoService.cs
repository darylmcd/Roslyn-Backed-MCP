using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Enumerates all loaded Roslyn analyzers and their supported diagnostic rules for a workspace.
/// </summary>
public interface IAnalyzerInfoService
{
    /// <summary>
    /// Lists all loaded analyzer assemblies and their supported diagnostic descriptors.
    /// </summary>
    Task<IReadOnlyList<AnalyzerInfoDto>> ListAnalyzersAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
