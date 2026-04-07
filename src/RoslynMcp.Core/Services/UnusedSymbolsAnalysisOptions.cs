namespace RoslynMcp.Core.Services;

/// <summary>
/// Controls which symbol kinds and project types are included in an unused-symbol scan.
/// </summary>
public sealed record UnusedSymbolsAnalysisOptions
{
    /// <summary>
    /// Optional project name filter. When non-null, only that project is scanned.
    /// </summary>
    public string? ProjectFilter { get; init; }

    /// <summary>
    /// When <see langword="true"/>, public symbols are included (default: <see langword="false"/>).
    /// Public API members may be consumed externally and are excluded by default to reduce noise.
    /// </summary>
    public bool IncludePublic { get; init; }

    /// <summary>Maximum number of results to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>When <see langword="true"/>, enum members are excluded (often referenced indirectly).</summary>
    public bool ExcludeEnums { get; init; }

    /// <summary>When <see langword="true"/>, properties on record types are excluded (typically DTO/serialization-shaped).</summary>
    public bool ExcludeRecordProperties { get; init; }

    /// <summary>When <see langword="true"/>, projects whose names match the test-project pattern are excluded.</summary>
    public bool ExcludeTestProjects { get; init; }

    /// <summary>When <see langword="true"/>, symbols in test fixture types are excluded.</summary>
    public bool ExcludeTests { get; init; }
}
