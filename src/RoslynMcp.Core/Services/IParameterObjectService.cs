using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// <c>parameter-object-preview-tool</c> (v1, experimental): groups N parameters of a
/// target method into a positional <c>record</c> DTO and rewrites every call site to
/// pass a single <c>new Dto(...)</c> argument in place of the grouped parameters. The
/// preview adds the new record file and rewrites both the method declaration and every
/// caller in one atomic snapshot, redeemable through the standard
/// <see cref="IRefactoringService.ApplyRefactoringAsync(string, string, System.Threading.CancellationToken)"/>
/// path. See <c>ai_docs/items/parameter-object-preview-design.md</c> for the contract.
/// </summary>
public interface IParameterObjectService
{
    /// <summary>
    /// Builds a preview that synthesizes a positional record DTO and atomically rewrites
    /// the target method's signature plus every call site to flow grouped argument values
    /// through the new record. Refuses the preview structurally for default-value,
    /// <c>ref</c>/<c>out</c>/<c>in</c>/<c>params</c>, extension <c>this</c>, local-function,
    /// and missing cross-project-reference cases — see the design note for the full list.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewParameterObjectAsync(
        string workspaceId,
        SymbolLocator target,
        ParameterObjectPreviewRequest request,
        CancellationToken ct);
}
