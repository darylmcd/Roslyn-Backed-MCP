# script-execution-supervisor-cancel-catch-observability — document or log cancel-race empty catch

**row:** `script-execution-supervisor-cancel-catch-observability` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs` (`AbandonWorkerOnHardDeadline`, line ~271)

## Acceptance

- [ ] Either add a bounded debug/trace log on `ObjectDisposedException` during `timeoutCts.Cancel()`, or document the race as an approved idempotent cleanup with an inline comment referencing this row
- [ ] No behavior change to script timeout/abandon semantics

## Evidence

- Surfaced by doc-audit bad-code scan (2026-06-11); empty `catch (ObjectDisposedException) { }` after cancel on hard deadline.

## Context

The catch is likely intentional (CTS already disposed when the worker finished), but it is silent today. Prefer a one-line approved-exception comment over new metrics unless operators report missing cancel diagnostics.