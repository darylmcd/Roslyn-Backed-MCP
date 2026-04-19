using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for bulk refactoring operations
/// such as replacing all occurrences of a type reference across the solution.
/// </summary>
public interface IBulkRefactoringService
{
    /// <summary>
    /// Previews replacing all references to one type with another across the solution.
    /// Can be scoped to parameters, fields, or all reference sites.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="oldTypeName">Fully qualified or simple name of the type to replace.</param>
    /// <param name="newTypeName">Fully qualified or simple name of the replacement type.</param>
    /// <param name="scope">
    /// Scope filter: "parameters", "fields", or "all" (default).
    /// The "parameters" scope covers method parameter declarations and also generic
    /// arguments appearing in implemented-interface / base-class declarations (e.g. the
    /// <c>T</c> in <c>class Foo : IValidateOptions&lt;T&gt;</c>) so the interface contract
    /// stays in sync with the parameter rewrites.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewBulkReplaceTypeAsync(
        string workspaceId, string oldTypeName, string newTypeName, string? scope, CancellationToken ct);

    /// <summary>
    /// Previews rewriting every call-site of a method to call a different method with a
    /// declared argument-reorder mapping. The <paramref name="oldMethod"/> and
    /// <paramref name="newMethod"/> parameters use the <c>FQ.TypeName.MethodName(P1,P2,P3)</c>
    /// syntax — the parameter-type list disambiguates overloads. The new method's parameter
    /// names must be drawn from the old method's parameter names; the position of each old
    /// parameter name in the new list describes the reorder. Every positional call is rewritten
    /// per the positional mapping; named-argument calls keep their names and the reorder only
    /// applies when the caller mixes positional + named args or uses only positional form.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="oldMethod">
    /// FQ signature of the method whose call-sites are being rewritten, in the form
    /// <c>Namespace.Type.Method(ParamType1, ParamType2, ...)</c>. The parameter-type list is
    /// required and disambiguates overloads.
    /// </param>
    /// <param name="newMethod">
    /// FQ signature of the replacement method, in the form
    /// <c>Namespace.Type.OtherMethod(ParamTypeA, ParamTypeB, ...)</c>. The new parameter-type
    /// list must be a permutation of the old list so a positional mapping can be derived.
    /// </param>
    /// <param name="scope">Reserved for future scope filters. Currently only <c>null</c>/<c>all</c> is accepted.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewReplaceInvocationAsync(
        string workspaceId, string oldMethod, string newMethod, string? scope, CancellationToken ct);
}
