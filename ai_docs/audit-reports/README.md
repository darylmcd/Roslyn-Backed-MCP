# MCP deep-review audit reports

<!-- purpose: Index for raw deep-review audit outputs and templates; points to baseline, static snapshots, and rollup handoff. -->

Use the living prompt [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) with an MCP client connected to **roslyn-mcp** to exercise the full tool surface against a real solution.

## What belongs here

- **Raw per-run audit files** produced by the deep-review prompt.
- **Baseline templates and point-in-time snapshots** for the audit program itself.

Use immutable raw file naming: `yyyyMMddTHHmmssZ_<repo-id>_mcp-server-audit.md`.

Do **not** store synthesized cross-repo actioning summaries here. Those belong in [`../reports/README.md`](../reports/README.md).

### Filenames

Raw audits use `<timestamp>_<repo-id>_mcp-server-audit.md` (no lock-mode segment). The server ships with a single per-workspace `AsyncReaderWriterLock` model — there is no second mode to record in the filename.

> Historical note: pre-cleanup audit files in this directory may carry a `_rw-lock_` or `_legacy-mutex_` segment from when the server supported a dual-mode lane. Those file names are immutable history; do not rename them. New audits drop the segment.

## Intake rule

- Keep raw audit files immutable once written.
- **Standard path:** run **`eng/new-deep-review-batch.ps1`** from the Roslyn-Backed-MCP root (no args). It pulls the newest `*_mcp-server-audit.md` per repo-id from sibling folders under the same parent directory (e.g. `C:\Code-Repo\*`), copies into this tree, writes a rollup, and updates `ai_docs/backlog.md` unless `-SkipBacklogSync` is passed.
- Manual import only if needed: `eng/import-deep-review-audit.ps1`.
- **2026-04-22 batch cleanup:** Timestamped raw files from 2026-04-13 / 15 / 22 (firewallanalyzer, itchatbot, networkdocumentation, Jellyfin stress) were read against the then-current server, their open **server-side** follow-ups were folded into `ai_docs/backlog.md` (P3: `validate-workspace-overallstatus-false-positive`, `workspace-close-missing-solution-on-disk`; P4: `mcp-audit-rollup-2026-04-13-22`), and the raw files were **removed** from this directory per the contract in `backlog.md` (Refs) — not because every finding was already implemented.

## Static files and patterns

| File or pattern | Purpose |
|-----------------|---------|
| `deep-review-session-checklist.md` | Fill-in worksheet for a live session; phase order tracks the living prompt (including Phase 8b and Phase 9 after 10). |
| `<timestamp>_<repo-id>_mcp-server-audit.md` | Raw evidence for one repo/client deep-review run. |

## Session worksheet

| File | Purpose |
|------|---------|
| [`deep-review-session-checklist.md`](deep-review-session-checklist.md) | Optional checklist to fill during an MCP session; canonical output is still a timestamped `*_mcp-server-audit.md`. |

For multi-repo campaigns, start with [`../procedures/deep-review-program.md`](../procedures/deep-review-program.md), write raw files here, then synthesize the batch in [`../reports/README.md`](../reports/README.md).

Re-run the deep-review prompt after major surface changes (see appendix version line in the prompt).
