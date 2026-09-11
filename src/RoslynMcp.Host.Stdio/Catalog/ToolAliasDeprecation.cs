using System.Diagnostics.CodeAnalysis;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Immutable lifecycle declaration for a callable deprecated tool alias. The same record is
/// published by the catalog and embedded in the alias's JSON response envelope, so replacement
/// guidance cannot drift between discovery and invocation.
/// </summary>
/// <param name="AliasName">The retained callable MCP tool name.</param>
/// <param name="CanonicalName">The MCP tool name clients should migrate to.</param>
/// <param name="Reason">Short human-readable explanation of why the alias exists.</param>
/// <param name="RiskBucket">
/// The preview/apply mutation-risk bucket, or <see langword="null"/> for an ordinary alias that
/// does not represent a preview/apply consolidation.
/// </param>
/// <param name="IntroducedRelease">The first released minor version that published the alias.</param>
/// <param name="EarliestRemovalMajor">The first major version in which removal is permitted.</param>
public sealed record ToolAliasDeprecation(
    string AliasName,
    string CanonicalName,
    string Reason,
    string? RiskBucket,
    string IntroducedRelease,
    int EarliestRemovalMajor)
{
    /// <summary>Standard reason text for sister-server-name aliases.</summary>
    public const string SisterServerReason = "alias for cross-MCP-server name compatibility";

    // Alias names first shipped in v1.33.0. They are ordinary read/analysis aliases, not
    // preview/apply consolidation routes, so RiskBucket remains null by policy.
    private static readonly IReadOnlyDictionary<string, ToolAliasDeprecation> s_byAlias =
        new Dictionary<string, ToolAliasDeprecation>(StringComparer.Ordinal)
        {
            ["get_symbol_outline"] = new(
                "get_symbol_outline",
                "document_symbols",
                SisterServerReason,
                RiskBucket: null,
                IntroducedRelease: "1.33.0",
                EarliestRemovalMajor: 2),
            ["find_duplicated_code"] = new(
                "find_duplicated_code",
                "find_duplicated_methods",
                SisterServerReason,
                RiskBucket: null,
                IntroducedRelease: "1.33.0",
                EarliestRemovalMajor: 2),
            ["get_test_coverage_map"] = new(
                "get_test_coverage_map",
                "test_coverage",
                SisterServerReason,
                RiskBucket: null,
                IntroducedRelease: "1.33.0",
                EarliestRemovalMajor: 2),
        };

    // Every legacy call site currently supplies the canonical name. Build this secondary index
    // once and fail startup if a future registry entry makes that mapping ambiguous.
    private static readonly IReadOnlyDictionary<string, ToolAliasDeprecation> s_byCanonical =
        s_byAlias.Values.ToDictionary(
            static deprecation => deprecation.CanonicalName,
            StringComparer.Ordinal);

    internal static bool TryGet(string toolName, [NotNullWhen(true)] out ToolAliasDeprecation? deprecation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return s_byAlias.TryGetValue(toolName, out deprecation);
    }

    /// <summary>
    /// Resolve the response envelope for an established sister-server alias call site that only
    /// carries its canonical target. The canonical index is deliberately unique so a new alias
    /// sharing a target must update its caller to use an explicit alias-name lookup.
    /// </summary>
    internal static ToolAliasDeprecation ForSisterAlias(string canonicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        return s_byCanonical.TryGetValue(canonicalName, out var deprecation)
            ? deprecation
            : throw new ArgumentOutOfRangeException(
                nameof(canonicalName),
                canonicalName,
                "The alias registry has no declaration for this canonical tool name.");
    }
}
