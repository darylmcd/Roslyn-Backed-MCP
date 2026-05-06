# Audit-Deep — Promotion-Only Mode

<!-- purpose: Thin mode wrapper around `full.md`. Pre-sets `mode=promotion-only`; skips Phase 6 entirely; focuses the coverage ledger on tier=experimental. -->

> **This is a thin wrapper.** The authoritative phase order, output schema, and tool-coverage rules live in [`full.md`](./full.md). Read it in full and run it verbatim, with the overrides below.

## Mode pre-set

- **`mode = promotion-only`** for this entire run. Do not let the operator override.
- Record this in the audit report header's *Mode* row exactly as `promotion-only`.

## Overrides vs `full.md`

1. **Skip Phase 6 entirely.** State `**N/A — skipped per mode=promotion-only**` in the Phase 6 section of the report and proceed directly from Phase 5 to Phase 7. Do **not** create a disposable worktree for this run; isolation is unnecessary because no apply-mode mutations run against the audited repo.
2. **Coverage ledger pre-filter.** When seeding the ledger from `roslyn://server/catalog` in Phase 0 step 11, mark every `tier=stable` row whose only role would have been a Phase 6 apply-mode probe as `skipped-safety — out of scope for promotion-only`. Stable tools that serve as scaffolding for an experimental probe (`workspace_load`, `find_unused_symbols` to set up `remove_dead_code_preview`, etc.) still get exercised.
3. **Promotion scorecard is the point.** Every experimental tool/resource/prompt must end with one of `promote` / `keep-experimental` / `needs-more-evidence` / `deprecate`. `blocked` rows are tracked in `summary.blocked` only.
4. **Promotion scorecard JSON is mandatory.** Write the sibling `_latest-promotion-scorecard.json` artifact per `full.md`'s *Output Format* — do not skip it.
5. **Phase 8b (concurrency).** Sequential baselines remain useful for promotion evidence. Parallel fan-out / read-write probes / writer reclassification (8b.5) are out of scope; mark `skipped-safety — promotion-only`.
6. **Phases 10 / 12 / 13 (file ops, scaffolding, project mutation).** Run preview-only. Apply siblings are `skipped-safety — promotion-only`.
7. **Phase 9 (undo verification).** Skip — there are no Phase 6 applies to verify against. Mark `skipped-safety — promotion-only`.

## Everything else

Follow `full.md` verbatim. Phase -1 hard gate, Phase 0 setup, Phases 1–5 / 7 / 8 / 8b.0–8b.1 / 11 / 14 / 15 / 16 / 16b / 17 / 18 / Final surface closure all run as written. The output schema in `full.md`'s *Output Format* is authoritative.

## Why this exists

Promotion-only runs are the operational input to `/release-cut`'s promotion gate. A short, scoped run focused on the experimental surface keeps the maintainer's context budget under control while still producing a fresh `_latest-promotion-scorecard.json` for the gate to consume.
