namespace RoslynMcp.Core.Models;

/// <summary>
/// Result payload for <c>ICodeActionService.GetCodeActionsAsync</c>. Wraps the action
/// list with a human-readable <see cref="Hint"/> when the list is empty so MCP callers
/// understand why nothing was returned (the position may not be on a fixable diagnostic
/// and no refactoring providers may apply to a single-token caret). FLAG-6B.
/// </summary>
/// <remarks>
/// The wrapping shape used to live inline in <c>CodeActionTools.GetCodeActions</c>
/// (a <c>System.Text.Json</c> anonymous object). Lifting it into a typed DTO lets
/// the MCP tool shim serialize the DTO through the ordinary
/// <c>ToolDispatch.ReadByWorkspaceIdAsync&lt;TDto&gt;</c> path without any per-tool
/// custom serialization. The serialized shape is preserved byte-identical:
/// <c>{ count, hint, actions }</c> in camelCase.
/// </remarks>
/// <param name="Count">Number of actions available at the requested span.</param>
/// <param name="Hint">
/// When <see cref="Actions"/> is empty, a guidance message explaining what the
/// caller might try next (widen the selection, point at a diagnostic).
/// <see langword="null"/> when at least one action is available.
/// </param>
/// <param name="Actions">The list of code actions at the span.</param>
public sealed record CodeActionListDto(
    int Count,
    string? Hint,
    IReadOnlyList<CodeActionDto> Actions);
