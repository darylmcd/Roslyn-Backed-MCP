# Audit-Deep — Read-Only Mode

<!-- purpose: Thin mode wrapper around `full.md`. Pre-sets `mode=read-only`; skips ALL apply-mode mutations and the promotion scorecard. -->

> **This is a thin wrapper.** The authoritative phase order, output schema, and tool-coverage rules live in [`full.md`](./full.md). Read it in full and run it verbatim, with the overrides below.

## Mode pre-set

- **`mode = read-only`** for this entire run. Do not let the operator override.
- Record this in the audit report header's *Mode* row exactly as `read-only`.

## Overrides vs `full.md`

1. **No applies anywhere.** Every `*_apply` tool call is forbidden. Every preview-only family is allowed. Every apply-only family (no preview sibling) is `skipped-safety — read-only`.
2. **Skip Phase 6 entirely.** State `**N/A — skipped per mode=read-only**` in the Phase 6 section. No Phase 6 sub-phases run, including the format/organize/rename/extract loops.
3. **Skip Phase 9 (undo verification).** No applies happened, so there is nothing to revert. Mark `skipped-safety — read-only`.
4. **Skip Phase 8b.5 (writer reclassification).** Writers cannot be exercised. Mark each row `skipped-safety — read-only`.
5. **Phases 10 / 12 / 13.** Preview-only. Apply siblings `skipped-safety — read-only`.
6. **No disposable worktree required.** Isolation is unnecessary — no tool call mutates the audited repo. Record the rationale in the report header's *Isolation* row as `N/A — read-only mode, no apply-mode mutations`.
7. **Promotion scorecard is skipped.** Read-only runs cannot produce apply-round-trip evidence, so writer recommendations would default to `needs-more-evidence` across the board. Writing a misleading scorecard is worse than writing none. Record in the audit report header *"Promotion scorecard skipped per mode=read-only"* and do **not** write `_latest-promotion-scorecard.json`.
8. **Coverage ledger.** Every write-capable family ends with `exercised-preview-only` (when its preview sibling ran) or `skipped-safety — read-only` (when the family has no preview sibling).
9. **Phase 17 negative probes.** Stale-token / version-mismatch probes that would normally use a real apply to advance workspace state should be marked `skipped-safety — read-only` (no real apply available to invalidate the token). Other negative-probe categories (invalid identifiers, out-of-range positions, empty inputs) run as written.

## Everything else

Follow `full.md` verbatim. Phase -1 hard gate, Phase 0 setup, Phases 1–5 / 7 / 8 (build/test reads only — `build_workspace` and `test_run` are reads, not applies) / 8b.0–8b.4 / 11 / 14 / 15 / 16 / 16b / 17 / 18 / Final surface closure all run as written. The output schema in `full.md`'s *Output Format* is authoritative — except section 12 (Experimental promotion scorecard) renders as a single `**N/A — skipped per mode=read-only**` line.

## Why this exists

Read-only runs are the safe option when no disposable checkout is available — for example, auditing a repo behind tight CI controls, or a quick health probe before deciding whether a deeper `mode=full` pass is justified. The output is a complete server-audit + skills-audit + prompt-verification report, minus Phase 6 product changes and the promotion scorecard.
