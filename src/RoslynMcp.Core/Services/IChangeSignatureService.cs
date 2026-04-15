using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 3 (v1.18, <c>change-signature-add-parameter-cross-callsite</c>): plumb Roslyn's
/// change-signature engine through the MCP surface so agents can add / remove / rename
/// method parameters with all callsites updated atomically.
/// </summary>
public interface IChangeSignatureService
{
    Task<RefactoringPreviewDto> PreviewChangeSignatureAsync(
        string workspaceId,
        SymbolLocator locator,
        ChangeSignatureRequest request,
        CancellationToken ct);
}

/// <summary>
/// Single change-signature operation. <see cref="Op"/> selects the kind:
/// <list type="bullet">
///   <item><description><c>add</c> — insert a new parameter at <see cref="Position"/> (or trailing if null). Requires <see cref="Name"/>, <see cref="ParameterType"/>; uses <see cref="DefaultValue"/> at every callsite.</description></item>
///   <item><description><c>remove</c> — drop the parameter at <see cref="Position"/>. Updates callsites by removing the matching positional or named argument.</description></item>
///   <item><description><c>rename</c> — change the parameter name (delegate to rename engine for callsites using named args). Requires <see cref="Name"/> (current) and <see cref="NewName"/>.</description></item>
/// </list>
/// Parameter reordering is not supported — callers that need it should issue a sequence of
/// remove + add ops via <c>symbol_refactor_preview</c>, or fall back to a multi-file edit.
/// </summary>
public sealed record ChangeSignatureRequest(
    string Op,
    string? Name = null,
    string? NewName = null,
    string? ParameterType = null,
    string? DefaultValue = null,
    int? Position = null);
