using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Discovers test methods in test projects and finds tests related to a given symbol or
/// set of changed source files using heuristic name matching.
/// </summary>
public interface ITestDiscoveryService
{
    Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct);

    /// <summary>
    /// Find tests related to <paramref name="locator"/>'s symbol. Returns the same envelope
    /// shape as <see cref="FindRelatedTestsForFilesAsync"/> — <c>tests</c>,
    /// <c>dotnetTestFilter</c>, <c>pagination</c> — so callers can route either response
    /// through the same downstream <c>test_run --filter</c> pipeline.
    /// </summary>
    Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
        string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct);

    Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
        string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct);
}
