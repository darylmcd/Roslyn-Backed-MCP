namespace RoslynMcp.Core.Services;

/// <summary>
/// Controls which projects and field-usage categories are scanned when finding
/// source-declared fields that are never read, never written, or never either.
/// </summary>
public sealed record DeadFieldsAnalysisOptions
{
    /// <summary>
    /// Optional project name filter. When non-null, only that project is scanned.
    /// </summary>
    public string? ProjectFilter { get; init; }

    /// <summary>
    /// When <see langword="true"/>, include public/protected fields whose deadness may be
    /// masked by external consumers. Default <see langword="false"/> keeps the results
    /// conservative for in-repo cleanup.
    /// </summary>
    public bool IncludePublic { get; init; }

    /// <summary>
    /// Optional usage-kind filter. Accepted values: <c>never-read</c>,
    /// <c>never-written</c>, <c>never-either</c>. When null, all three are returned.
    /// </summary>
    public string? UsageKindFilter { get; init; }

    /// <summary>Maximum number of hits to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
