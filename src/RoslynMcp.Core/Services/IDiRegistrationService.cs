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

    async Task<DiRegistrationScanResult> GetDiRegistrationsDetailedAsync(
        string workspaceId,
        string? projectFilter,
        bool includeOverrideChains,
        CancellationToken ct)
    {
        if (includeOverrideChains)
        {
            return await GetDiRegistrationsWithOverridesAsync(workspaceId, projectFilter, ct)
                .ConfigureAwait(false);
        }

        var registrations = await GetDiRegistrationsAsync(workspaceId, projectFilter, ct)
            .ConfigureAwait(false);
        return new DiRegistrationScanResult(
            registrations,
            OverrideChains: [],
            IsComplete: true,
            FailedDocumentCount: 0);
    }

    /// <summary>
    /// di-lifetime-mismatch-detection: extended scan that returns the same flat registration
    /// list plus a per-service-type override chain. Computed only when the caller opts in via
    /// the <c>showLifetimeOverrides</c> tool parameter — the default scan path skips the chain
    /// projection so payload shape stays stable.
    /// </summary>
    Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
