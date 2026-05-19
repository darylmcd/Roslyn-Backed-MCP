# Backlog sweep plan — 20260519T145945Z

**Generated:** 2026-05-19T14:59:46Z
**Backlog snapshot:** 2026-05-19T13:48:28Z (`ai_docs/backlog.md` updated_at)
**Schema version:** 3 (prepare-extended)
**Initiative count:** 6 (all Low / P3-shaped from 2026-05-16 self-audit)

## Selection notes

count=15 cap requested; only 6 concrete rows are sweep-actionable today.

Excluded:
- 2 aggregator rows (`firewallanalyzer-p2-polish-aggregate-20260516` Medium, `firewallanalyzer-p3-polish-aggregate-20260516` Low) — backlog standing rule says replace umbrella rows with concrete follow-ons **before** planning against them.
- 1 track-only row (`tool-surface-pagination-or-tool-sets`) — weak-evidence; act-on triggers haven't fired.
- 4 `Reserved` good-first-issue rows — skipped per planner Step 1.
- 6 `Defer` rows — explicitly parked with re-evaluate conditions.

No Rule 1 bundle candidates — each row has its own distinct code path / service file. Cross-checked against the two most recent state.json files (20260518T221744Z, 20260517T235058Z) — no overlap with shipped initiatives.

All 6 selected rows are P3-priority single-bug fixes from the 2026-05-16 self-audit on roslyn-mcp 1.38.1.

## Initiatives

### 1. workspace-changes-atomic-batch-split-without-batchid

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 2. validate-workspace-changetracker-no-disk-reconcile-after-git-checkout

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 3. find-type-mutations-single-scope-misses-compound-io

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 4. analyze-control-flow-partial-slice-warning-on-full-method

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 5. format-document-preview-empty-diff-instead-of-noop

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 6. callers-callees-previewtext-asymmetry

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

## Conflict graph

_Pending — Phase C computes after Phase B._

## Review

_Pending — Phase D runs after Phase C._
