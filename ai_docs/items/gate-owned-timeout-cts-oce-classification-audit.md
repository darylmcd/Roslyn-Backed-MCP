# gate-owned-timeout-cts-oce-classification-audit — audit other internal-timeout CTS sites for unclassified-OCE escape

**row:** `gate-owned-timeout-cts-oce-classification-audit` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ApplyUndoWorkflowService.cs:139` (`_revertBudget`)
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:90` (20s `CancelAfter`)
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs:32` (20s `CancelAfter`)
- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs:92` (`budget`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:518` (`timeoutMinutes`)

## Acceptance

- [ ] For each anchor: confirm whether a linked/internal `CancellationTokenSource` combining the caller's token with its own timeout can throw an `OperationCanceledException` that is NOT reclassified into a non-OCE exception (e.g. `TimeoutException`) before it reaches a caller
- [ ] Where the gap exists AND the call path can reach `StructuredCallToolFilter` (i.e., it's reachable from a tool invocation, not purely internal), apply the same `catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested) => throw new TimeoutException(...)` pattern already used by `GatedCommandExecutor.ExecuteAsync`, `WorkspaceValidationService`, and (as of `test-run-unfiltered-bare-error-rootcause`) `WorkspaceExecutionGate`
- [ ] Where the gap doesn't apply (e.g., prompts never reach `StructuredCallToolFilter`, or the CTS is caller-token-only with no distinct internal deadline), note why and close without a code change

## Evidence

`test-run-unfiltered-bare-error-rootcause` (2026-08-11 post-review investigation) found that `WorkspaceExecutionGate.RunPerWorkspaceAsync`/`RunLoadGateAsync` let their own per-request-timeout `OperationCanceledException` (armed on a `CancellationTokenSource` distinct from the caller's ambient token) escape unclassified all the way to the MCP SDK's own top-level `tools/call` catch-all, which only recognizes cancellation tied to the AMBIENT per-request token — producing the exact bare `"An error occurred invoking '{tool}'."` fallback string (zero category, zero schemaHint, zero `_meta`) reported across multiple retros. `GatedCommandExecutor.ExecuteAsync` and `WorkspaceValidationService` already reclassify correctly at their own internal-timeout boundaries; `WorkspaceExecutionGate` was the one gap, now fixed. The five anchors above were found via `grep -rn "new CancellationTokenSource(|CancelAfter("  src/` and have NOT been individually checked for the same gap — this row is a scoped audit pass, not a presumption that each is broken.

## Context

Grep used: `grep -rn "new CancellationTokenSource\(|CancelAfter\(" src/` across the whole `src/` tree, cross-referenced against the confirmed-safe pattern (`GatedCommandExecutor.cs:95`, `WorkspaceValidationService.cs:938`, and the new `WorkspaceExecutionGate.cs` catches).
