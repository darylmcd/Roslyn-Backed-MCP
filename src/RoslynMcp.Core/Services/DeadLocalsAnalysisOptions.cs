namespace RoslynMcp.Core.Services;

/// <summary>
/// Controls which projects are scanned for dead method-local variables (locals whose
/// only write is not followed by any read).
/// </summary>
public sealed record DeadLocalsAnalysisOptions
{
    /// <summary>
    /// Optional project name filter. When non-null, only that project is scanned.
    /// </summary>
    public string? ProjectFilter { get; init; }

    /// <summary>Maximum number of hits to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
