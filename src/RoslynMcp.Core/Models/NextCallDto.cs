namespace RoslynMcp.Core.Models;

/// <summary>
/// A structured, machine-actionable follow-up tool call an agent can issue verbatim,
/// replacing prose-only "call X with Y" hints.
/// </summary>
/// <param name="Tool">The MCP tool name to call next (e.g. <c>workspace_reload</c>).</param>
/// <param name="Arguments">The exact argument names and values to pass to <paramref name="Tool"/>.
/// Keys are the tool's wire (camelCase) parameter names and are serialized as-is.</param>
public sealed record NextCallDto(
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments)
{
    /// <summary>
    /// The structured recovery call for a workspace whose package restore is required:
    /// <c>workspace_reload</c> with <c>autoRestore=true</c> for <paramref name="workspaceId"/>.
    /// Shared by every result that reports <c>restoreRequired</c> so the wire literal lives in one place.
    /// </summary>
    public static NextCallDto WorkspaceReloadWithAutoRestore(string workspaceId) =>
        new(
            "workspace_reload",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId, ["autoRestore"] = true });
}
