# workspace-load-abstract-lane-internal-error — Diagnose the captured lane-load failure

**row:** `workspace-load-abstract-lane-internal-error` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:396-436` — confirmed workspace_load boundary; underlying failing stage is unknown.

## Acceptance

- [ ] Diagnose the actual cause of the captured valid workspace_load failure, add a deterministic red-first regression in the owning loader fixture, and prove successful workspace publication plus ordinary/cancellation controls without weakening gates or changing stable public schemas/categories/messages. Record base/head evidence; a successful retry alone is not root-cause proof.

## Evidence

- Trigger -> harm: head legacy host request40 loads the owned RoslynMcp.slnx with autoRestore:false and returns InternalError before publishing a workspace, blocking complete lane wire qualification. Impact P2; reach L1; origin unknown; I5 plus mandatory tool-defect filing.
- 2026-11-25T20:05:56Z; correlation a9c7c6eb26bd4cdd813d71436ac1c2fd; heldMs2921/elapsedMs2922. Exact public error: 'An unexpected internal error occurred. Retry once; if the failure repeats, report the correlation ID to the server operator.' This quote is evidence, not permission to retry a red gate.
- Stderr: 'workspace_load boundary: outcome=failed reused=False projectCount=(null) durationMs=2919'; 'Tool workspace_load failed; outcome=unexpected-error; elapsedMs=2922'. No underlying exception, stack or causal stage is retained.
- The other three base/head protocol combinations loaded the same lane with nine projects and1191 documents. They establish neither a harmless flake nor an environment cause nor a reproduced base failure. No retry/fallback occurred.
- Raw/stderr: ~/.codex/scratch/roslyn-remediate-20261125/abstract-implementations-implement/head-2025-11-25-raw.jsonl and head-2025-11-25-stderr.log; counterpart captures retain controls.

## Context

- The affected dual-era done-check remains I4 until properly discharged; filing does not turn the failure green. No causal connection to the separate constructed-handle defect is established.
- Cross-reference candidates only: workspace-load-logging-failure-ownership; workspace-cache-coordinator-unexpected-failure-propagation; workspace-load-unresolved-sdk-diagnostic; net10-host-msbuild-locator-sdk-11-workspaces. Deduplicate only after proving an actual shared cause.
- Size M is provisional investigation scope, not an invented production file list. Choose the real owning source and regression fixture after diagnosis. Do not guess a loader fix or add a major-only error-text enhancement.
