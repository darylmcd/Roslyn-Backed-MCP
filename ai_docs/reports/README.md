# AI reports

<!-- purpose: Index for synthesized rollups and cross-cutting audit reports that drive actioning and backlog intake. -->

Use this folder for **synthesized** outputs that combine or interpret multiple raw inputs.

## What belongs here

- Cross-repo deep-review rollups built from raw files in `../audit-reports/`
- Cross-cutting audit reports such as test-suite reviews
- Actioning summaries that decide what should or should not reach `ai_docs/backlog.md`

## What does not belong here

- Raw per-repo deep-review prompt outputs. Store those in `../audit-reports/`.

## Deep-review rollup minimum contents

Use immutable naming: `yyyyMMddTHHmmssZ_deep-review-rollup.md`.

Each rollup should include:

| Section | Must include |
|---------|--------------|
| Scope | Batch date, clients, server/catalog version, campaign purpose |
| Inputs | Raw audit file list from `../audit-reports/` |
| Repo matrix coverage | Covered buckets and missing buckets |
| Client coverage | Full-surface vs constrained lanes and blocked families |
| Concurrency matrix rollup | Aggregated `parallel_speedup` numbers per repo pulled from each raw audit's *Concurrency matrix → Parallel fan-out* table. Required when ≥2 input audits ran Phase 8b. |
| Performance baseline rollup | Aggregated p50/p90 `_meta.elapsedMs` per tool across repos. Drives "is tool X within budget on real-world solutions" decisions. |
| Experimental promotion rollup | Per-experimental-entry recommendation aggregated across repos. Feeds `docs/experimental-promotion-analysis.md`. Flag any entry with `deprecate` or repeated FAIL findings. |
| Prompt verification rollup | Aggregated prompt-verification counts: exercised, blocked, hallucinated tools, idempotency failures. |
| Skills audit rollup | Required when any input ran Phase 16b. Any invalid tool reference is a plugin ship blocker. |
| Deduped issues | Unique defect key (`tool + symptom + catalog-version + client-family`) and linked evidence |
| Candidate closures | Prior ids and current evidence |
| Backlog actions | Rows to open, update, or intentionally leave out |

**Backlog intake:** by default, `eng/new-deep-review-batch.ps1` runs `eng/sync-deep-review-backlog.ps1` so new §14-style findings merge into `ai_docs/backlog.md`. Add rows by hand for narrative-only sources (e.g. test-suite audits) when needed — see [`../procedures/deep-review-backlog-intake.md`](../procedures/deep-review-backlog-intake.md).

**Rollup-only:** `eng/new-deep-review-rollup.ps1 -AuditFiles <paths...>`

## Current files

| File | Purpose |
|------|---------|
| `reports/2026-04-06-deep-review-rollup-example.md` | Concrete example of a synthesized deep-review rollup with deduped issues, blocked-client summary, and backlog actions. |
| `reports/2026-04-06-test-suite-audit.md` | Cross-cutting audit of the test suite's speed, SRP, and setup costs. |
| `reports/20260413T164400Z_deep-review-rollup.md` | Deep-review rollup for the 2026-04-13 batch (scaffold + input audit manifest). |
| `reports/20260415T153515Z_deep-review-rollup.md` | Deep-review rollup for the 2026-04-15 batch. |
| `reports/20260422T143323Z_deep-review-rollup.md` | Deep-review rollup for the 2026-04-22 batch. |

For the full deep-review workflow, start with `../procedures/deep-review-program.md`.
