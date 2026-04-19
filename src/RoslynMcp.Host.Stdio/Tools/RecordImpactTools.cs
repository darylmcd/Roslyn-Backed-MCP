using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Tools that audit record-shape changes (positional field addition, etc.). Distinct from
/// <see cref="ImpactSweepTools"/> (which handles general symbol-impact sweeps) because record-shape
/// breakage has its own structural categories — pattern matches, deconstructions, <c>with</c>
/// expressions — that need separate buckets in the response.
/// </summary>
[McpServerToolType]
public static class RecordImpactTools
{
    [McpServerTool(Name = "preview_record_field_addition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "experimental", true, false,
        "Pre-flight audit: every site impacted by adding a positional field to a record."),
     Description("Pre-flight audit for adding a positional field to a record. Returns positional construction sites (with rewritten arg lists), deconstruction sites (with rewritten patterns), property-pattern sites (flagged when exhaustive-in-spirit but missing the new field), `with`-expression sites, and test files that mention the record. Catches the breaking-change shapes the C# compiler does NOT flag: pattern coverage, deconstruction shape, with-expression assumptions.")]
    public static Task<string> PreviewRecordFieldAddition(
        IWorkspaceExecutionGate gate,
        IRecordFieldAdditionService recordFieldAdditionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified metadata name of the target record (e.g. 'SampleLib.MyRecord')")] string recordMetadataName,
        [Description("The proposed new positional field name (PascalCase, valid C# identifier)")] string newFieldName,
        [Description("The proposed new field type display string (e.g. 'bool', 'System.Guid', 'string?')")] string newFieldType,
        [Description("Optional default-value expression to splice into rewritten construction sites (e.g. 'false', 'Guid.Empty'). When null, rewrites use a /* TODO */ placeholder.")] string? defaultValue = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var dto = await recordFieldAdditionService.PreviewAdditionAsync(
                workspaceId, recordMetadataName, newFieldName, newFieldType, defaultValue, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }
}
