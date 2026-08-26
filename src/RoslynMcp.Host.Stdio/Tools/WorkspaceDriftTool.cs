using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry point for <c>workspace_drift_check</c>. Wraps
/// <see cref="IWorkspaceDriftService.CheckDriftAsync"/> with the standard read-gated
/// dispatch so multiple drift checks run concurrently against the same workspace and so
/// an in-flight write (<c>*_apply</c>) blocks the check until it completes.
/// </summary>
/// <remarks>
/// Marked experimental on first ship: the response shape (Stale / FilesDrifted /
/// Recommended) and the recommendation string vocabulary may evolve as agents start
/// branching on it (e.g. adding a third "partial-reload" recommendation if/when the
/// reload path supports per-document refresh).
/// </remarks>
[McpServerToolType]
public static class WorkspaceDriftTool
{
    [McpServerTool(Name = "workspace_drift_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceDriftResult)),
     McpToolMetadata("workspace", "experimental", true, false,
        "Compare the in-memory workspace snapshot against filesystem mtimes; return drift status, drifted file paths, and a reload/noop recommendation."),
     Description("Fast probe that compares the in-memory MSBuildWorkspace snapshot for `workspaceId` against the current on-disk last-write times of every tracked document. Returns `{ stale: bool, filesDrifted: string[], recommended: 'reload' | 'noop' }`. A document drifts when its mtime is past the workspace's loadedAtUtc, or when the file no longer exists on disk (deletion is also drift). Agents call this BEFORE a read tool to decide whether `workspace_reload` is needed — eliminates the dilemma between always-reloading (slow) and never-reloading (silent stale reads after out-of-band Edit/Write mutations). Source-generated documents have no file path and are skipped. Output is deterministic: the `filesDrifted` list is sorted ordinally and deduped across linked documents.")]
    public static Task<CallToolResult> WorkspaceDriftCheck(
        IWorkspaceExecutionGate gate,
        IWorkspaceDriftService driftService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        CancellationToken ct = default)
        => gate.RunReadAsync(
            workspaceId,
            async c => StructuredToolResult.Create(
                await driftService.CheckDriftAsync(workspaceId, c).ConfigureAwait(false)),
            ct);
}
