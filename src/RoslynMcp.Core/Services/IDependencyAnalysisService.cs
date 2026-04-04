using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Analyzes NuGet package references, project dependency graphs, namespace dependencies,
/// and dependency injection registrations across a loaded workspace.
/// </summary>
public interface IDependencyAnalysisService
{
    Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    /// <summary>
    /// Scans NuGet package references for known vulnerabilities using <c>dotnet list package --vulnerable</c>.
    /// </summary>
    Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct);
}
