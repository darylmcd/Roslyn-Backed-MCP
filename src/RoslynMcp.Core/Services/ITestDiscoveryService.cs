using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Discovers test methods in test projects and finds tests related to a given symbol or
/// set of changed source files using heuristic name matching.
/// </summary>
public interface ITestDiscoveryService
{
    Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct);
}
