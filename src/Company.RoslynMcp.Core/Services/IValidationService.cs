using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IValidationService
{
    Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct);

    Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct);

    Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct);

    Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);
}
