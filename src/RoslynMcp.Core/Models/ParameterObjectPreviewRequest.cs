namespace RoslynMcp.Core.Models;

/// <summary>
/// Request payload for <c>parameter_object_preview</c>: groups N parameters of a target
/// method into a generated positional <c>record</c> DTO and rewrites every call site so
/// the original argument values flow through the new record's primary constructor.
/// </summary>
/// <remarks>
/// v1 is opinionated: the generated DTO is always a <c>sealed record</c> with positional
/// primary-constructor parameters mirroring <see cref="ParameterNames"/> in caller-supplied
/// order. Default-value omissions and by-ref parameters refuse the preview rather than
/// risk semantic drift. See <c>ai_docs/items/parameter-object-preview-design.md</c> for
/// the full contract.
/// </remarks>
/// <param name="ParameterNames">
/// Ordered list of parameter names on the target method to group into the new record.
/// Must contain at least two entries; each must exist on the target. Order is preserved
/// in the generated record's primary constructor.
/// </param>
/// <param name="NewTypeName">PascalCase identifier for the generated record type.</param>
/// <param name="DtoProjectName">
/// Optional override for the project the new record lands in. When omitted the record
/// is added to the same project as the target method. When supplied and different from
/// the method's project, every project containing a call site MUST already reference
/// the override project — v1 does not auto-insert <c>&lt;ProjectReference&gt;</c> entries.
/// </param>
/// <param name="DtoNamespace">
/// Optional namespace override for the generated record. Defaults to the method's
/// containing namespace.
/// </param>
/// <param name="DtoFolders">
/// Optional folder override (relative to the target project root). Defaults to folders
/// derived from <see cref="DtoNamespace"/> via the same convention as
/// <c>scaffold_type_preview</c>.
/// </param>
/// <param name="ParameterName">
/// Optional name for the new single parameter on the rewritten method. Defaults to
/// <see cref="NewTypeName"/> camelCased.
/// </param>
public sealed record ParameterObjectPreviewRequest(
    IReadOnlyList<string> ParameterNames,
    string NewTypeName,
    string? DtoProjectName = null,
    string? DtoNamespace = null,
    IReadOnlyList<string>? DtoFolders = null,
    string? ParameterName = null);
