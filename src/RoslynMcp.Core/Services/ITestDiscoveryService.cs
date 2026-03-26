using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface ITestDiscoveryService
{
    Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct);
}
