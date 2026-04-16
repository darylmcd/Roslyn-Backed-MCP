using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 9: guided sweep after an impactful symbol change. Bundles references, switch
/// exhaustiveness diagnostics, and mapper-suffix callsites into a single structured response.
/// </summary>
[McpServerToolType]
public static class ImpactSweepTools
{
    [McpServerTool(Name = "symbol_impact_sweep", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "experimental", true, false,
        "Sweep downstream impact of a symbol change: references + switch-exhaustiveness diagnostics + mapper-suffix callsites."),
     Description("Sweep downstream impact of a symbol change: references, non-exhaustive switches (CS8509/CS8524/IDE0072), and mapper/converter-suffix callsites. Returns suggested-tasks list. Pass `summary=true` (drops per-ref preview text) and/or `maxItemsPerCategory` (caps each list) for high-fan-out symbols where the default response exceeds the MCP cap (Jellyfin's 1452-ref BaseItem: ~886 KB).")]
    public static Task<string> SweepSymbolImpact(
        IWorkspaceExecutionGate gate,
        IImpactSweepService impactSweepService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the symbol")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name (preferred when caller knows the symbol shape)")] string? metadataName = null,
        [Description("When true, drops per-reference preview text to keep the response small for high-fan-out symbols. File path + line + column + classification still populated. Default false preserves the v1.18 shape.")] bool summary = false,
        [Description("Optional cap on items per category (References, MapperCallsites, SwitchExhaustivenessIssues). Use together with summary=true on extremely high-fan-out symbols.")] int? maxItemsPerCategory = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = new SymbolLocator(filePath, line, column, symbolHandle, metadataName);
            locator.Validate();
            var dto = await impactSweepService.SweepAsync(workspaceId, locator, c, summary, maxItemsPerCategory).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }
}
