---
category: Maintenance
---

- **Maintenance:** Audited the five remaining internal-timeout `CancellationTokenSource` sites for the unclassified-`OperationCanceledException` escape that was fixed in `WorkspaceExecutionGate`. **Four of five reproduce no gap**, each for a distinct architectural reason, recorded here because the audit shipped no code and its plan directory is garbage-collected on closure:
  1. `ApplyUndoWorkflowService.BestEffortRevertAfterCancellationAsync` — the internal-budget CTS is used only inside a `catch (Exception)` that logs and never rethrows, so its OCE cannot escape. The only OCE leaving `ApplyWithVerifyAsync` is tied to the caller's ambient token.
  2. `RoslynPrompts` (20s `CancelAfter`) — already catches with the two-condition guard and returns a friendly `PromptMessage`; additionally an `[McpServerPrompt]`, so it never reaches `StructuredCallToolFilter`'s `tools/call` catch-all.
  3. `RoslynPrompts.RefactoringWorkflows` (20s `CancelAfter`) — same shape, same double safety.
  4. `ScriptExecutionSupervisor` — architecturally immune rather than guarded: the internal-timeout token is observed only by a synchronous worker delegate that converts OCE into a `ScriptExecutionOutcome.TimedOut()` **result value**; the sole throwing path is registered against the caller's ambient token. Worth citing as a model alternate pattern.
  5. `WorkspaceForkApplyService.RestoreForkAsync` — reclassifies, but with a **weaker one-condition guard** (`when (!ct.IsCancellationRequested)`) rather than the canonical two-condition form used by `GatedCommandExecutor` / `WorkspaceExecutionGate`. The original audit recorded this site as "mirroring `GatedCommandExecutor`", which is not accurate. Tracked as `workspace-fork-oce-guard-single-condition`.
