namespace RoslynMcp.Core.Services;

/// <summary>
/// Controls scope and thresholds for <see cref="IDuplicateMethodDetectorService"/> scans.
/// </summary>
public sealed record DuplicateMethodAnalysisOptions
{
    /// <summary>
    /// Minimum body line count for a method to be considered. Bodies below this length
    /// are ignored entirely — they produce too many false-positive clusters
    /// (one-line delegate passthroughs, auto-generated setters, etc.). Defaults to 10.
    /// </summary>
    public int MinLines { get; init; } = 10;

    /// <summary>
    /// Minimum pairwise similarity (0.0–1.0) for a group to be emitted. Exact
    /// structural duplicates score 1.0. Lower values admit more near-matches at
    /// the cost of noise. Defaults to 0.85.
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.85;

    /// <summary>
    /// Optional project name filter. When non-null, only that project is scanned.
    /// Useful for large solutions where a full-solution AST sweep is too expensive.
    /// </summary>
    public string? ProjectFilter { get; init; }

    /// <summary>Maximum number of groups to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
