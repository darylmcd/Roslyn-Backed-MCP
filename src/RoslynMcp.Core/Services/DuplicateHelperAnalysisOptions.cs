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

    /// <summary>
    /// When <see langword="true"/> (default), omit hits whose sole delegation target is declared
    /// in a framework-glue namespace — thin forwarders to ASP.NET Core HTTP primitives
    /// (<c>Microsoft.AspNetCore.Http.Results</c>, minimal-API shapes) or <c>System.Net.Http</c>
    /// (<c>HttpClient</c> helpers). Those wrappers are often load-bearing (endpoint binding,
    /// test harnesses) rather than redundant reinventions of BCL primitives. Set to
    /// <see langword="false"/> to restore the pre-filter behavior.
    /// </summary>
    public bool ExcludeFrameworkWrappers { get; init; } = true;
}
