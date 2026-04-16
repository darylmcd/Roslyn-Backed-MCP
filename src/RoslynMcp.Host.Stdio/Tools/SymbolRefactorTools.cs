using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 7 (v1.18, <c>agent-symbol-refactor-unified-preview</c>): one composite preview that
/// chains rename + edit + restructure operations in order. Each op sees the rewritten state
/// from earlier ops; the final accumulator is stored under one preview token.
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
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var dto = await symbolRefactorService.PreviewAsync(workspaceId, operations ?? [], c).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }
}
