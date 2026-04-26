namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// roslyn-mcp-sister-tool-name-aliases: response-envelope payload published on every alias-tool
/// JSON response (and emitted as <c>null</c> on the canonical tool's response so the schema
/// always carries the field). When non-null, <see cref="CanonicalName"/> is the underlying
/// MCP tool the agent should migrate to; <see cref="Reason"/> documents why the alias exists.
/// </summary>
/// <param name="CanonicalName">The canonical MCP tool name the alias delegates to.</param>
/// <param name="Reason">Short human-readable explanation of why the alias is published.</param>
internal sealed record ToolAliasDeprecation(string CanonicalName, string Reason)
{
    /// <summary>
    /// Standard reason text for sister-server-name aliases. Centralized so every alias method
    /// emits the same message and a single edit propagates to the whole alias surface.
    /// </summary>
    public const string SisterServerReason = "alias for cross-MCP-server name compatibility";

    /// <summary>
    /// Build a sister-server alias deprecation envelope for the given canonical tool name.
    /// </summary>
    public static ToolAliasDeprecation ForSisterAlias(string canonicalName) =>
        new(canonicalName, SisterServerReason);
}
