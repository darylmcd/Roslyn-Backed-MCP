using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IDependencyAnalysisService
{
    Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
