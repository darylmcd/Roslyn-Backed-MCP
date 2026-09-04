using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    /// <summary>Reverts the latest solution-snapshot apply for a workspace.</summary>
    /// <remarks>
    /// Reversion restores text snapshots but does not delete files created as refactoring side
    /// effects. Use <c>delete_file_apply</c> on each created file to complete that recovery.
    /// </remarks>
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "stable", false, true,
        "Revert the most recent Roslyn solution-level apply operation for a workspace."),
     Description("SINGLE-SLOT LIFO: reverts only the latest apply; a second call reports 'No operation to revert'. For earlier workspace_changes entries, use revert_apply_by_sequence. Does not remove created files.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("workspaceId is required. Pass the session id returned by workspace_load.");
        }
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var entry = undoService.GetLastOperation(workspaceId);
            if (entry is null)
            {
                var nothingResult = new
                {
                    reverted = false,
                    message = "No operation to revert. Nothing has been applied in this session, " +
                              "or the workspace was reloaded / closed and re-loaded since the last apply."
                };
                return JsonSerializer.Serialize(nothingResult, JsonDefaults.Indented);
            }

            var success = await undoService.RevertAsync(workspaceId, c).ConfigureAwait(false);
            if (!success)
            {
                var failResult = new
                {
                    reverted = false,
                    message = "Failed to revert — the workspace state may have changed since the operation was applied.",
                    operation = entry.Description
                };
                return JsonSerializer.Serialize(failResult, JsonDefaults.Indented);
            }

            var result = new
            {
                reverted = true,
                revertedOperation = entry.Description,
                appliedAtUtc = entry.AppliedAtUtc
            };
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "revert_apply_by_sequence", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "stable", false, true,
        "Revert a specific earlier apply identified by its workspace_changes sequence number."),
     Description("Revert a specific earlier solution-snapshot apply by its workspace_changes sequence number. Refuses when a later apply touched the same file; revert blocking sequences first.")]
    public static Task<string> RevertApplyBySequence(
        IWorkspaceExecutionGate gate,
        IApplyUndoWorkflowService workflowService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("The sequence number reported by workspace_changes for the apply you want to revert")] int sequenceNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("workspaceId is required. Pass the session id returned by workspace_load.");
        }
        if (sequenceNumber <= 0)
        {
            throw new ArgumentException("sequenceNumber must be a positive integer matching a value reported by workspace_changes.", nameof(sequenceNumber));
        }
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await workflowService.RevertBySequenceAsync(workspaceId, sequenceNumber, c).ConfigureAwait(false);

            // Shape the response: include `reason`/`blockingSequences` only when present so success
            // payloads stay clean. Failure payloads always carry `reason`.
            object payload = result.Reverted
                ? new
                {
                    reverted = true,
                    revertedOperation = result.RevertedOperation,
                    affectedFiles = result.AffectedFiles,
                    sequenceNumber
                }
                : result.BlockingSequences is { Count: > 0 }
                    ? new
                    {
                        reverted = false,
                        reason = result.Reason,
                        revertedOperation = result.RevertedOperation,
                        affectedFiles = result.AffectedFiles,
                        sequenceNumber,
                        blockingSequences = result.BlockingSequences,
                        message = "Cannot revert: a later apply touches one of the same files. " +
                                  "Revert the listed blocking sequences first, then retry."
                    }
                    : new
                    {
                        reverted = false,
                        reason = result.Reason,
                        revertedOperation = result.RevertedOperation,
                        affectedFiles = result.AffectedFiles,
                        sequenceNumber,
                        message = result.Reason switch
                        {
                            "unknown-sequence" => "No revert snapshot exists for that sequence number. " +
                                                  "Either the sequence is from before this session, or the apply did not produce a revertable snapshot.",
                            "revert-failed" => "The snapshot was located but disk/workspace mechanics rejected the revert.",
                            _ => "Revert failed."
                        }
                    };
            return JsonSerializer.Serialize(payload, JsonDefaults.Indented);
        }, ct);
    }
}
