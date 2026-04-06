# MCP deep-review audit reports

<!-- purpose: Index for raw deep-review audit outputs and templates; points to baseline, static snapshots, and rollup handoff. -->

Use the living prompt [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) with an MCP client connected to **roslyn-mcp** to exercise the full tool surface against a real solution.

## What belongs here

- **Raw per-run audit files** produced by the deep-review prompt.
- **Baseline templates and point-in-time snapshots** for the audit program itself.

Use immutable raw file naming: `yyyyMMddTHHmmssZ_<repo-id>_mcp-server-audit.md`.

Do **not** store synthesized cross-repo actioning summaries here. Those belong in [`../reports/README.md`](../reports/README.md).

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
