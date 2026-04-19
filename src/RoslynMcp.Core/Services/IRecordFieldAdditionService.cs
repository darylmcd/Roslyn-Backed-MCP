using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Pre-flight audit for adding a positional field to a record. Positional record changes are a
/// canonical source of silent breakage: the C# compiler catches positional-ctor argument mismatches
/// but not deconstruction shape, property-pattern coverage, or <c>with</c>-expression assumptions.
/// This service classifies every reachable site so a reviewer can plan the change before applying it.
/// </summary>
public interface IRecordFieldAdditionService
{
    /// <summary>
    /// Computes a <see cref="RecordFieldAdditionImpactDto"/> for the named record. Resolves the
    /// record by metadata name, walks the cross-solution references, and classifies each one
    /// into the five impact buckets.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier from <c>workspace_load</c>.</param>
    /// <param name="recordMetadataName">Fully qualified metadata name of the target record (e.g. <c>SampleLib.MyRecord</c>).</param>
    /// <param name="newFieldName">The proposed new positional field name (PascalCase, valid C# identifier).</param>
    /// <param name="newFieldType">The proposed new field type (display string, e.g. <c>bool</c>, <c>System.Guid</c>).</param>
    /// <param name="defaultValueExpression">
    /// Optional default-value expression. When provided, suggested rewrites splice this in;
    /// otherwise rewrites use a <c>/* TODO */</c> placeholder.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown when no type can be resolved for <paramref name="recordMetadataName"/> in the workspace.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the resolved type is not a record (record class or record struct).
    /// </exception>
    Task<RecordFieldAdditionImpactDto> PreviewAdditionAsync(
        string workspaceId,
        string recordMetadataName,
        string newFieldName,
        string newFieldType,
        string? defaultValueExpression,
        CancellationToken ct);
}
