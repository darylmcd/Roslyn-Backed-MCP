namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of <c>preview_record_field_addition</c>: every site impacted by adding a positional
/// field to a positional record. Each bucket represents a structurally distinct breakage that
/// the C# compiler would NOT catch on its own (positional ctors are caught — pattern-match,
/// deconstruction, and <c>with</c>-shape sites are NOT, beyond the trivial cases).
/// </summary>
/// <param name="TargetRecordDisplay">Fully qualified display name of the analyzed record.</param>
/// <param name="IsPositionalRecord">
/// <c>true</c> when the record has a primary constructor (positional fields). When <c>false</c>,
/// only <see cref="WithExpressionSites"/> are populated — adding a property to a non-positional
/// record only breaks <c>with { ... }</c> consumers that explicitly omit the new property when
/// the property is required.
/// </param>
/// <param name="NewField">The proposed added field (name, type, optional default expression).</param>
/// <param name="ExistingPositionalParameters">
/// The current positional parameter list of the target record, in declaration order. Empty when
/// <see cref="IsPositionalRecord"/> is <c>false</c>.
/// </param>
/// <param name="PositionalConstructionSites">
/// Every <c>new TargetRecord(arg1, arg2, ...)</c> call site whose argument count matches the
/// existing positional ctor (these will fail to compile after the field is added unless an
/// extra argument is passed). Each entry includes a suggested rewritten argument list.
/// </param>
/// <param name="DeconstructionSites">
/// Every <c>var (a, b) = record;</c> deconstruction call site (and corresponding switch /
/// is-pattern positional-deconstruction sites). Each entry includes a suggested rewritten
/// pattern with a placeholder for the new field.
/// </param>
/// <param name="PropertyPatternSites">
/// Every <c>record is { Foo: x, Bar: y }</c> property-pattern site that references the target
/// record. Flagged with <c>MissedCorrelation</c> when the pattern is exhaustive in spirit
/// (uses every existing field) but does not yet name the new field — these are the high-signal
/// correlations a reviewer should audit.
/// </param>
/// <param name="WithExpressionSites">
/// Every <c>existing with { ... }</c> site. Always populated regardless of
/// <see cref="IsPositionalRecord"/> because <c>with</c>-expressions work on every record kind
/// and are sensitive to required-property changes.
/// </param>
/// <param name="TestFilesConstructing">
/// Distinct test file paths (under any <c>*.Tests</c> project, or files matching <c>*Tests.cs</c>
/// / <c>*Spec.cs</c>) that mention the target record. Test sites are typically the densest
/// remediation cluster and benefit from being grouped separately so reviewers can plan a
/// single test-fixture sweep.
/// </param>
/// <param name="SuggestedTasks">
/// Human-readable remediation hints derived from the populated buckets — e.g. how many sites
/// need an updated argument list, how many patterns may be missing the new field.
/// </param>
public sealed record RecordFieldAdditionImpactDto(
    string TargetRecordDisplay,
    bool IsPositionalRecord,
    NewRecordFieldDto NewField,
    IReadOnlyList<ExistingPositionalParameterDto> ExistingPositionalParameters,
    IReadOnlyList<RecordPositionalConstructionSiteDto> PositionalConstructionSites,
    IReadOnlyList<RecordDeconstructionSiteDto> DeconstructionSites,
    IReadOnlyList<RecordPropertyPatternSiteDto> PropertyPatternSites,
    IReadOnlyList<RecordWithExpressionSiteDto> WithExpressionSites,
    IReadOnlyList<string> TestFilesConstructing,
    IReadOnlyList<string> SuggestedTasks);

/// <summary>The proposed new positional field on the target record.</summary>
/// <param name="Name">The new field/property name (PascalCase, valid C# identifier).</param>
/// <param name="Type">The new field/property type (display string, e.g. <c>bool</c>, <c>System.Guid</c>).</param>
/// <param name="DefaultValueExpression">
/// Optional default-value expression to splice into rewritten construction sites
/// (e.g. <c>false</c>, <c>null</c>, <c>Guid.Empty</c>). When <c>null</c>, the rewritten
/// argument list uses the placeholder <c>/* TODO: NewField */</c>.
/// </param>
public sealed record NewRecordFieldDto(string Name, string Type, string? DefaultValueExpression);

/// <summary>One existing positional parameter on the target record.</summary>
/// <param name="Name">The parameter name (matches the auto-generated property name).</param>
/// <param name="Type">The parameter type display string.</param>
public sealed record ExistingPositionalParameterDto(string Name, string Type);

/// <summary>
/// One <c>new TargetRecord(...)</c> construction site whose argument count matches the existing
/// positional constructor. Carries a suggested rewritten argument list with the new field
/// inserted at the end.
/// </summary>
/// <param name="Location">Source location of the construction expression.</param>
/// <param name="OriginalArgumentList">The original argument list as written, e.g. <c>(1, "x")</c>.</param>
/// <param name="SuggestedArgumentList">The suggested rewritten argument list, e.g. <c>(1, "x", false)</c>.</param>
public sealed record RecordPositionalConstructionSiteDto(
    LocationDto Location,
    string OriginalArgumentList,
    string SuggestedArgumentList);

/// <summary>
/// One deconstruction site of the form <c>var (a, b) = record;</c>, including
/// positional patterns inside <c>switch</c> / <c>is</c>.
/// </summary>
/// <param name="Location">Source location of the deconstruction expression / pattern.</param>
/// <param name="OriginalPattern">The original deconstruction shape, e.g. <c>(a, b)</c>.</param>
/// <param name="SuggestedPattern">The suggested rewritten shape with the new field, e.g. <c>(a, b, _)</c>.</param>
public sealed record RecordDeconstructionSiteDto(
    LocationDto Location,
    string OriginalPattern,
    string SuggestedPattern);

/// <summary>
/// One property-pattern site of the form <c>record is { Foo: x, Bar: y }</c>.
/// </summary>
/// <param name="Location">Source location of the property pattern.</param>
/// <param name="OriginalPattern">The original property-pattern shape (e.g. <c>{ Foo: x, Bar: y }</c>).</param>
/// <param name="MissedCorrelation">
/// <c>true</c> when the pattern names every existing positional field (suggesting it was meant
/// to be exhaustive) but does not yet name the new field. These are the highest-signal sites
/// for reviewer attention.
/// </param>
public sealed record RecordPropertyPatternSiteDto(
    LocationDto Location,
    string OriginalPattern,
    bool MissedCorrelation);

/// <summary>One <c>existing with { ... }</c> site.</summary>
/// <param name="Location">Source location of the <c>with</c> expression.</param>
/// <param name="OriginalExpression">The original <c>with</c> initializer block (e.g. <c>{ Foo = 1 }</c>).</param>
public sealed record RecordWithExpressionSiteDto(
    LocationDto Location,
    string OriginalExpression);
