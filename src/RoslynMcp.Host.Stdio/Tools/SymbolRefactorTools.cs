using System.ComponentModel;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 7 (v1.18, <c>agent-symbol-refactor-unified-preview</c>): one composite preview that
/// chains rename + edit + restructure operations in order. Each op sees the rewritten state
/// from earlier ops; the final accumulator is stored under one preview token. WS1 phase 1.6
/// — each shim body delegates to <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>
/// instead of carrying the dispatch boilerplate inline.
/// </summary>
[McpServerToolType]
public static class SymbolRefactorTools
{
    [McpServerTool(Name = "symbol_refactor_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Composite preview chaining rename + edit + restructure operations into a single token."),
     Description("Composite preview that chains heterogeneous refactor operations (rename, multi-file edit, structural rewrite) in order. Operations are atomic: a failure in any step aborts the whole preview. Each operation is { kind: 'rename'|'edit'|'restructure', ... }. See ISymbolRefactorService for the per-kind field shape.")]
    public static Task<string> PreviewSymbolRefactor(
        IWorkspaceExecutionGate gate,
        ISymbolRefactorService symbolRefactorService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Ordered list of operations. Each is { kind: 'rename'|'edit'|'restructure', ...kind-specific fields }. Order matters — later ops see earlier ops' rewritten state.")] SymbolRefactorOperation[] operations,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => symbolRefactorService.PreviewAsync(workspaceId, operations ?? [], c),
            ct);

    [McpServerTool(Name = "split_service_with_di_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Composite preview splitting a service into partitions + forwarding facade + DI-registration deltas."),
     Description("Composite preset (composite-split-service-di-registration) built on symbol_refactor_preview primitives: splits a service type into N partition implementations + a forwarding facade, and emits DI-registration deltas (Transient/Scoped/Singleton inferred from the existing registration) in one preview token. When the host registration file is null or the registration is missing, the preview falls back to a warning rather than crashing.")]
    public static Task<string> PreviewSplitServiceWithDi(
        IWorkspaceExecutionGate gate,
        ISymbolRefactorService symbolRefactorService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type to split")] string sourceFilePath,
        [Description("Name of the concrete type to split (e.g., 'Foo')")] string sourceType,
        [Description("Partition specs: each { typeName, memberNames[] } — every MemberName must exist on sourceType and each member must belong to exactly one partition.")] SplitServicePartition[] partitions,
        [Description("Absolute path to the file containing the existing services.Add* registration for sourceType; pass null to scan the workspace for any matching registration.")] string? hostRegistrationFile = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => symbolRefactorService.PreviewSplitServiceWithDiAsync(
                workspaceId, sourceFilePath, sourceType, partitions ?? [], hostRegistrationFile, c),
            ct);

    [McpServerTool(Name = "record_field_add_with_satellites_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Composite preview synthesizing coordinated edits for satellite members when a type gains a new field."),
     Description("Composite preview (record-field-add-satellite-member-sync) that inspects a type's existing fields, infers the satellite-sync pattern (Clone/Snapshot/With/ToJson/Increment), and proposes edits for every mirror site when a new field is added. Pattern inference is conservative: requires ≥2 sibling fields with identical satellite coverage before declaring a pattern — returns an empty preview with `patternDetectionReason` populated otherwise. Redeem the `previewToken` via apply_composite_preview.")]
    public static Task<string> PreviewRecordFieldAddWithSatellites(
        IWorkspaceExecutionGate gate,
        ISymbolRefactorService symbolRefactorService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified metadata name of the target record/class (e.g. 'SampleLib.Counters')")] string typeMetadataName,
        [Description("The new field name (valid C# identifier)")] string newFieldName,
        [Description("The new field type display string (e.g. 'int', 'string', 'System.Guid')")] string newFieldType,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => symbolRefactorService.PreviewRecordFieldAddWithSatellitesAsync(
                workspaceId, typeMetadataName, newFieldName, newFieldType, c),
            ct);
}
