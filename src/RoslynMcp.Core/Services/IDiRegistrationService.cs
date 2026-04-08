using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Scans the workspace for dependency-injection registration patterns
/// (<c>services.AddSingleton</c>, <c>AddScoped</c>, <c>AddTransient</c>, etc.) and reports
/// the service / implementation type pairs along with their declared lifetime.
/// </summary>
public interface IDiRegistrationService
{
    Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
