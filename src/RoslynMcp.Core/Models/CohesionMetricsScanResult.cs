namespace RoslynMcp.Core.Models;

/// <summary>
/// Cohesion scan output plus an explicit completeness contract. A non-zero failed type count
/// means the metrics are useful partial evidence, not a complete workspace result.
/// </summary>
public sealed record CohesionMetricsScanResult(
    IReadOnlyList<CohesionMetricsDto> Metrics,
    bool IsComplete,
    int FailedTypeCount);
