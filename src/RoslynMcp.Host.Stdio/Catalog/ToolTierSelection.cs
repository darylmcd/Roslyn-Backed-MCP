namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Parses and applies the operator-selected MCP surface support tiers. The default retains the
/// complete public surface; an explicit <c>stable</c> selection provides a lean profile for
/// clients that eagerly load discovery definitions.
/// </summary>
internal sealed class ToolTierSelection
{
    internal const string EnvironmentVariableName = "ROSLYNMCP_TOOL_TIERS";

    private static readonly string[] s_validTiers = ["stable", "experimental"];
    private readonly HashSet<string> _tiers;

    private ToolTierSelection(IEnumerable<string> tiers)
    {
        _tiers = new HashSet<string>(tiers, StringComparer.Ordinal);
    }

    public static ToolTierSelection All { get; } = new(s_validTiers);

    public IReadOnlyCollection<string> Tiers => _tiers;

    public bool Includes(string tier) => _tiers.Contains(tier);

    internal static bool IsSupportedTier(string tier) =>
        s_validTiers.Contains(tier, StringComparer.Ordinal);

    public static ToolTierSelection Parse(string? value)
    {
        if (value is null)
        {
            return All;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidValue(value);
        }

        var tokens = value.Split(',', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(static token => token.Length == 0))
        {
            throw InvalidValue(value);
        }

        var normalized = tokens
            .Select(static token => token.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Any(static token => token is not ("stable" or "experimental"))
            || !normalized.Contains("stable", StringComparer.Ordinal))
        {
            throw InvalidValue(value);
        }

        return new ToolTierSelection(normalized);
    }

    public override string ToString() => string.Join(',', s_validTiers.Where(Includes));

    private static ArgumentException InvalidValue(string? value) =>
        new(
            $"Invalid {EnvironmentVariableName} value '{value}'. " +
            "Use 'stable' or 'stable,experimental'; experimental tools require the stable baseline.",
            nameof(value));
}
