using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "stable", false, true,
        "Revert the most recent Roslyn solution-level apply operation for a workspace."),
     Description("Revert the most recent apply operation for a workspace, restoring the previous solution state. Coverage: renames, code fixes, format, organize usings, apply_text_edit / apply_multi_file_edit, create_file / delete_file / move_file / extract_interface_apply / extract_type_apply / move_type_to_file_apply (Item #2 — 2026-04-16). Project-file (csproj) mutations route through Item #5's SDK-style-safe apply path, which restores the pre-apply csproj bytes as part of the apply itself; on revert the csproj is already in its pre-apply state. Limitation: reverts text edits only — it does NOT remove files created as side effects (e.g. extracted files from extract_type_apply / extract_method_apply / extract_interface_apply). Pair with delete_file_apply on the extracted file to fully undo a file-creating refactor. workspaceId is required.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
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
     Description("Revert a specific earlier apply by its sequence number — the value reported by workspace_changes. Complements revert_last_apply: instead of LIFO, this revert targets any historical apply still in this session's revert history. Conservative dependency check: if a later apply touched any file that the target apply also touched, the revert is blocked and the offending later sequence numbers are returned in `blockingSequences` so the caller can revert them first. workspaceId is required. sequenceNumber must match a value returned by workspace_changes for this workspace. Returns `{reverted, revertedOperation, affectedFiles, reason?, blockingSequences?}` — `reason` is `unknown-sequence` when the sequence has no recorded snapshot, `dependency-blocked` when a later apply overlaps in files, and `revert-failed` when the snapshot exists but disk/workspace mechanics rejected the revert.")]
    public static Task<string> RevertApplyBySequence(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
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
            var result = await undoService.RevertBySequenceAsync(workspaceId, sequenceNumber, c).ConfigureAwait(false);

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
