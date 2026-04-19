namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of <c>record_field_add_with_satellites_preview</c>: coordinated edit suggestions for
/// adding a new field to a type whose existing fields participate in a satellite-sync convention
/// (Clone / Snapshot / With / ToJson / Increment mirror types or methods).
///
/// <para>
/// Pattern inference is intentionally conservative — the service requires at least two sibling
/// fields sharing identical satellite coverage before declaring the set as "the pattern". When
/// fewer than two siblings agree, <see cref="InferredPattern"/> is empty and
/// <see cref="PatternDetectionReason"/> explains why so the caller gets a signal rather than a
/// false positive. Prefer false-negative over false-positive for this tool.
/// </para>
/// </summary>
/// <param name="TargetTypeDisplay">
/// Fully qualified display name of the analyzed target type (the record/class that is gaining
/// a field).
/// </param>
/// <param name="NewField">The proposed added field (name + type signature).</param>
/// <param name="InferredPattern">
/// The satellite-site kinds that were detected as "the pattern" — i.e. present across ≥2 sibling
/// fields with identical coverage. Empty when the pattern could not be inferred (see
/// <see cref="PatternDetectionReason"/>). Each entry is a structural label such as
/// <c>"CloneMethodBody"</c>, <c>"SnapshotType.Field"</c>, <c>"WithMethod.Assignment"</c>,
/// <c>"IncrementMethod"</c>, <c>"ToJson.Case"</c>.
/// </param>
/// <param name="PatternDetectionReason">
/// Human-readable explanation when <see cref="InferredPattern"/> is empty (e.g. "only one sibling
/// field detected", "sibling fields have divergent satellite coverage"). Empty string when a
/// pattern was inferred.
/// </param>
/// <param name="ProposedEdits">
/// Ordered list of edits the caller should review/apply. Each entry names the file, the
/// zero-anchored insertion location, the satellite-site kind it addresses, and the new text to
/// splice in. When <see cref="InferredPattern"/> is empty this list is also empty.
/// </param>
/// <param name="PreviewToken">
/// Composite preview token redeemable via <c>apply_composite_preview</c>. The token is issued
/// only when <see cref="InferredPattern"/> is non-empty; empty previews return <c>null</c> here
/// because there is nothing to apply.
/// </param>
/// <param name="Summary">
/// Human-readable one-line summary ("3 satellite sites + 1 mirror type field declaration" or
/// "pattern not inferred — 1 sibling field only").
/// </param>
public sealed record RecordFieldAddSatelliteDto(
    string TargetTypeDisplay,
    NewSatelliteFieldDto NewField,
    IReadOnlyList<string> InferredPattern,
    string PatternDetectionReason,
    IReadOnlyList<RecordFieldAddSatelliteEditDto> ProposedEdits,
    string? PreviewToken,
    string Summary);

/// <summary>The proposed new field signature.</summary>
/// <param name="Name">The field/property name (valid C# identifier).</param>
/// <param name="Type">The field/property type display string (e.g. <c>int</c>, <c>string</c>).</param>
public sealed record NewSatelliteFieldDto(string Name, string Type);

/// <summary>
/// One edit proposed by <see cref="RecordFieldAddSatelliteDto.ProposedEdits"/>. Edits are
/// insert-only (the tool synthesizes new satellite members; it never removes existing ones).
/// </summary>
/// <param name="FilePath">Absolute path of the file to edit.</param>
/// <param name="Line">1-based line number of the insertion anchor.</param>
/// <param name="Column">1-based column number of the insertion anchor.</param>
/// <param name="NewText">The exact text to splice at the anchor.</param>
/// <param name="SiteKind">
/// Structural label identifying which satellite site this edit addresses — one of the entries
/// in <see cref="RecordFieldAddSatelliteDto.InferredPattern"/>. Useful for callers that want to
/// selectively apply a subset of edits.
/// </param>
/// <param name="Description">Short human-readable description of the edit (e.g. "Add NewField property to Snapshot").</param>
public sealed record RecordFieldAddSatelliteEditDto(
    string FilePath,
    int Line,
    int Column,
    string NewText,
    string SiteKind,
    string Description);
