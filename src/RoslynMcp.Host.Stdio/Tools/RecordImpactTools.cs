using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Tools that audit record-shape changes (positional field addition, etc.). Distinct from
/// <see cref="ImpactSweepTools"/> (which handles general symbol-impact sweeps) because record-shape
/// breakage has its own structural categories — pattern matches, deconstructions, <c>with</c>
/// expressions — that need separate buckets in the response. WS1 phase 1.6 — the single shim
/// body delegates to <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>.
/// </summary>
[McpServerToolType]
public static class RecordImpactTools
{
    /// <remarks>
    /// Construction sites carry rewritten argument lists, deconstruction sites carry rewritten
    /// patterns, and property-pattern sites are flagged when exhaustive-in-spirit but missing the
    /// new field. Test files that merely mention the record are reported separately.
    /// </remarks>
    [McpServerTool(Name = "preview_record_field_addition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "experimental", true, false,
        "Pre-flight audit: every site impacted by adding a positional field to a record."),
     Description("Pre-flight audit for adding a positional field to a record: construction, deconstruction, property-pattern, and `with`-expression sites. Catches breaking shapes the C# compiler does NOT flag.")]
    public static Task<string> PreviewRecordFieldAddition(
        IWorkspaceExecutionGate gate,
        IRecordFieldAdditionService recordFieldAdditionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified metadata name of the target record (e.g. 'SampleLib.MyRecord')")] string recordMetadataName,
        [Description("The proposed new positional field name (PascalCase, valid C# identifier)")] string newFieldName,
        [Description("The proposed new field type display string (e.g. 'bool', 'System.Guid', 'string?')")] string newFieldType,
        [Description("Optional default-value expression to splice into rewritten construction sites (e.g. 'false', 'Guid.Empty'). When null, rewrites use a /* TODO */ placeholder.")] string? defaultValue = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => recordFieldAdditionService.PreviewAdditionAsync(
                workspaceId, recordMetadataName, newFieldName, newFieldType, defaultValue, c),
            ct);
}
