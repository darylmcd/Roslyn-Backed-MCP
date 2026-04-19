namespace RoslynMcp.Core.Services;

/// <summary>
/// Controls which projects are scanned for private/internal extension-method helpers
/// whose body-shape duplicates a canonical BCL or NuGet symbol.
/// </summary>
public sealed record DuplicateHelperAnalysisOptions
{
    /// <summary>
    /// Optional project name filter. When non-null, only that project is scanned.
    /// </summary>
    public string? ProjectFilter { get; init; }

    /// <summary>Maximum number of hits to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
