using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 7 (v1.18, <c>agent-symbol-refactor-unified-preview</c>): chains heterogeneous refactor
/// operations (rename + multi-file edits + structural rewrites) into a single composite preview
/// token. Operations run sequentially against an accumulating Solution snapshot so later ops
/// see the text produced by earlier ops.
/// </summary>
public interface ISymbolRefactorService
{
    Task<RefactoringPreviewDto> PreviewAsync(
        string workspaceId,
        IReadOnlyList<SymbolRefactorOperation> operations,
        CancellationToken ct);

    /// <summary>
    /// Composite preset (<c>composite-split-service-di-registration</c>): splits a service type
    /// into N partition implementations plus a forwarding facade and emits DI-registration
    /// deltas against the host registration file in one composite preview token.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by workspace_load.</param>
    /// <param name="sourceFilePath">Absolute path to the source file containing the type to split.</param>
    /// <param name="sourceType">Name of the concrete type to split (e.g., <c>Foo</c>).</param>
    /// <param name="partitions">
    /// Partition specs: each <see cref="SplitServicePartition"/> lists the new type name and the
    /// member names from <paramref name="sourceType"/> that should move into that partition.
    /// Members not listed in any partition remain on the forwarding facade as thin pass-through
    /// members (currently this means all facade members forward to a partition — members must
    /// be covered by exactly one partition).
    /// </param>
    /// <param name="hostRegistrationFile">
    /// Absolute path to the file containing the existing <c>services.Add*</c> registration for
    /// <paramref name="sourceType"/>. When null or the registration is not found the orchestration
    /// falls back to a warning rather than crashing — the preview still includes the partition
    /// file creations and facade rewrite.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewSplitServiceWithDiAsync(
        string workspaceId,
        string sourceFilePath,
        string sourceType,
        IReadOnlyList<SplitServicePartition> partitions,
        string? hostRegistrationFile,
        CancellationToken ct);

    /// <summary>
    /// <c>record-field-add-satellite-member-sync</c>: synthesizes coordinated edits for satellite
    /// members when a type (record/class) adds a new field. Inspects existing sibling fields,
    /// infers the satellite-coverage pattern (<c>Clone</c> / <c>Snapshot</c> / <c>With</c> /
    /// <c>ToJson</c> / <c>Increment</c> mirror types or methods) conservatively — requires ≥2
    /// sibling fields with identical coverage before declaring the pattern — and returns an edit
    /// list plus composite preview token.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier from <c>workspace_load</c>.</param>
    /// <param name="typeMetadataName">
    /// Fully qualified metadata name of the target record/class (e.g. <c>SampleLib.Counters</c>).
    /// </param>
    /// <param name="newFieldName">The new field name (valid C# identifier).</param>
    /// <param name="newFieldType">
    /// The new field type display string (e.g. <c>int</c>, <c>string</c>, <c>System.Guid</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="RecordFieldAddSatelliteDto"/> whose <c>InferredPattern</c> is empty (and
    /// <c>PatternDetectionReason</c> is populated) when the pattern cannot be inferred
    /// conservatively — callers use the reason to decide whether to fall back to manual edits
    /// or to adjust the target type.
    /// </returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown when no type resolves for <paramref name="typeMetadataName"/>.
    /// </exception>
    Task<RecordFieldAddSatelliteDto> PreviewRecordFieldAddWithSatellitesAsync(
        string workspaceId,
        string typeMetadataName,
        string newFieldName,
        string newFieldType,
        CancellationToken ct);
}

/// <summary>
/// One step in a composite refactor. <see cref="Kind"/> selects the shape of the remaining
/// fields:
/// <list type="bullet">
///   <item><description><c>rename</c> — supply <see cref="SymbolHandle"/> (or <see cref="FilePath"/> + <see cref="Line"/> + <see cref="Column"/>) and <see cref="NewName"/>.</description></item>
///   <item><description><c>edit</c> — supply <see cref="FileEdits"/> for direct multi-file text edits.</description></item>
///   <item><description><c>restructure</c> — supply <see cref="Pattern"/> + <see cref="Goal"/> (and optional <see cref="ScopeFilePath"/> / <see cref="ScopeProjectName"/>).</description></item>
/// </list>
/// </summary>
public sealed record SymbolRefactorOperation(
    string Kind,
    string? SymbolHandle = null,
    string? FilePath = null,
    int? Line = null,
    int? Column = null,
    string? MetadataName = null,
    string? NewName = null,
    IReadOnlyList<FileEditsDto>? FileEdits = null,
    string? Pattern = null,
    string? Goal = null,
    string? ScopeFilePath = null,
    string? ScopeProjectName = null);

/// <summary>
/// Describes a single partition for <see cref="ISymbolRefactorService.PreviewSplitServiceWithDiAsync"/>.
/// </summary>
/// <param name="TypeName">
/// The name of the new type (implementation class) that will host the listed members. The
/// preset derives the partition's interface name as <c>I</c> + <paramref name="TypeName"/>.
/// </param>
/// <param name="MemberNames">
/// The member names (methods / properties) from the source type that should move into this
/// partition. Each listed member must exist on the source type.
/// </param>
public sealed record SplitServicePartition(
    string TypeName,
    IReadOnlyList<string> MemberNames);
