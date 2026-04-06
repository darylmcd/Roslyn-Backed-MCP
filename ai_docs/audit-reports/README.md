# MCP deep-review audit reports

<!-- purpose: Index for raw deep-review audit outputs and templates; points to baseline, static snapshots, and rollup handoff. -->

Use the living prompt [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) with an MCP client connected to **roslyn-mcp** to exercise the full tool surface against a real solution.

## What belongs here

- **Raw per-run audit files** produced by the deep-review prompt.
- **Baseline templates and point-in-time snapshots** for the audit program itself.

Use immutable raw file naming: `yyyyMMddTHHmmssZ_<repo-id>_mcp-server-audit.md`.

Do **not** store synthesized cross-repo actioning summaries here. Those belong in [`../reports/README.md`](../reports/README.md).

### Lock-mode tagging and filenames

Every raw audit produced after the addition of Phase 8b populates two pieces of lock-mode metadata, both auto-filled by the deep-review prompt:

1. **Filename:** `<timestamp>_<repo-id>_<lockMode>_mcp-server-audit.md`. The `<lockMode>` segment is `legacy-mutex` or `rw-lock`, derived from the agent's Phase 0 step 4 `evaluate_csharp` call (which reads `ROSLYNMCP_WORKSPACE_RW_LOCK` from inside the running server). The agent never asks the operator for the filename.
2. **Header field `Lock mode at audit time`:** the same value as the filename segment, set automatically by the agent.

The legacy `dual-mode (legacy-mutex → rw-lock)` header value is no longer used for fresh audits — see *Auto-pair model* below for the replacement.

### Auto-pair model — every raw audit is one of three shapes

Phase 8b cannot toggle the lock mode mid-session, so a complete dual-mode lane is produced by running the prompt twice: once per mode, with `eng/flip-rw-lock.ps1` between the runs. The agent auto-pairs the runs by globbing `ai_docs/audit-reports/*_<repo-id>_<oppositeMode>_mcp-server-audit.md` in Phase 0 step 15.

| Shape | When it is produced | Concurrency matrix state | Notes |
|-------|---------------------|----------------------------|-------|
| **Mode-N partial (run 1 of a planned pair, or single-mode lane)** | Operator runs the prompt once. Phase 0 step 15 finds no opposite-mode partial to pair with. | Session A columns populated; Session B columns marked `skipped-pending-second-run`. | The `<lockMode>` segment in the filename matches whichever mode the server was in. The **Report path note** instructs the operator to run `./eng/flip-rw-lock.ps1`, restart the MCP client, and re-invoke the prompt to pair this file. |
| **Run-2 dual-mode (canonical)** | Operator runs the prompt a second time after `flip-rw-lock.ps1` and a client restart. Phase 0 step 15 finds the run-1 partial. | Session A columns inherited from the run-1 partial; Session B columns measured in this run. **Both columns populated in this single file.** | This is the canonical dual-mode raw audit. The **Report path note** points at the run-1 partial it paired with. The run-1 file remains on disk as immutable run-1 evidence; the run-2 file is the input the rollup pipeline uses for dual-mode coverage. |
| **Single-mode (operator never plans a second run)** | Operator runs the prompt once and explicitly tells the agent this is a single-mode lane. | Session A columns populated; Session B columns marked `skipped-single-mode-lane`. | Identical filename shape to the run-1 partial above; the only difference is the **Report path note** wording. Acceptable for routine smoke runs; not acceptable for release-candidate runs (see `../procedures/deep-review-program.md`). |

Rollups must dedupe lock-mode-specific defects by `tool + symptom + catalog-version + client-family + lock-mode` (see `../procedures/deep-review-program.md`). Run-2 dual-mode files are the preferred rollup input because they contain both modes' evidence in one place; run-1 partials are still valid inputs when used in pairs alongside their run-2 counterpart, but the rollup tool should prefer the run-2 file when both exist for the same `<repo-id>`.

## Intake rule

- Keep raw audit files immutable once written.
- Do **not** open backlog rows directly from a multi-repo batch of raw audits.
- First create a synthesized rollup in `ai_docs/reports/`, then open or update `ai_docs/backlog.md` from that rollup.
- If a raw file was produced in another workspace, import it with `eng/import-deep-review-audit.ps1` before including it in a rollup.

## Static files and patterns

| File or pattern | Purpose |
|-----------------|---------|
| `deep-review-baseline-2026-04-04.md` | Structure and checklist for a full audit; update when the tool/resource/prompt surface changes. |
| `2026-04-04-post-1.5-surface-audit.md` | Post-1.5 lightweight surface audit (catalog, promotions, tests, profiling notes). |
| `<timestamp>_<repo-id>_mcp-server-audit.md` | Raw evidence for one repo/client deep-review run. |

## Baseline session template

| File | Purpose |
|------|---------|
| [`deep-review-baseline-2026-04-04.md`](deep-review-baseline-2026-04-04.md) | Structure and checklist for a full audit; update when the tool/resource/prompt surface changes. |
| [`2026-04-04-post-1.5-surface-audit.md`](2026-04-04-post-1.5-surface-audit.md) | Post-1.5 lightweight surface audit (catalog, promotions, tests, profiling notes). |

For multi-repo campaigns, start with [`../procedures/deep-review-program.md`](../procedures/deep-review-program.md), write raw files here, then synthesize the batch in [`../reports/README.md`](../reports/README.md).

Re-run the deep-review prompt after major surface changes (see appendix version line in the prompt).
