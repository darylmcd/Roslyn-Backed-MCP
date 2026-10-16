using RoslynMcp.Host.Stdio.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Offline version-check state explicitly supplied by each test.</summary>
internal sealed class FixedLatestVersionProvider(
    string? latest,
    VersionCheckStatus status,
    DateTime? lastCheckedAt) : ILatestVersionProvider
{
    public string? Latest { get; set; } = latest;
    public VersionCheckStatus LastCheckStatus { get; set; } = status;
    public DateTime? LastCheckedAt { get; set; } = lastCheckedAt;

    public string? GetLatestVersion() => Latest;
}
