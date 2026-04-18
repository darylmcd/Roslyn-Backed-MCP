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

    /// <summary>
    /// di-lifetime-mismatch-detection: extended scan that returns the same flat registration
    /// list plus a per-service-type override chain. Computed only when the caller opts in via
    /// the <c>showLifetimeOverrides</c> tool parameter — the default scan path skips the chain
    /// projection so payload shape stays stable.
    /// </summary>
    Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
