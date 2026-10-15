using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Preview method parameter add/remove/rename/reorder, and add/remove class or struct primary-constructor parameters, with bound callsites updated atomically. Records and primary-constructor rename/reorder remain unsupported.
/// Struct last-parameter removal uses an explicit marker constructor; adding to an explicit zero constructor retains it as the initialization owner.
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
///   <item><description><c>remove</c> — drop the parameter at <see cref="Position"/>. Updates callsites by removing every argument bound to that parameter, including expanded params elements; body/capture/base references must remain valid.</description></item>
///   <item><description><c>rename</c> — change the parameter name (delegate to rename engine for callsites using named args). Requires <see cref="Name"/> (current) and <see cref="NewName"/>.</description></item>
///   <item><description><c>reorder</c> — permute parameters via <see cref="NewOrder"/> (comma-separated list of parameter names OR 0-based indices that must be a permutation of the existing parameters). Declaration is reordered, positional callsites are named to retain their original evaluation order and parameter bindings, named-only callsites retain their argument order, omitted compiler-supplied values are materialized, and mixed positional+named callsites raise an actionable error.</description></item>
/// </list>
/// </summary>
public sealed record ChangeSignatureRequest(
    string Op,
    string? Name = null,
    string? NewName = null,
    string? ParameterType = null,
    string? DefaultValue = null,
    int? Position = null,
    string? NewOrder = null);
