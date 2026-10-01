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
    IReadOnlyDictionary<string, object?> Arguments);
