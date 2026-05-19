# Backlog sweep plan — 20260519T193650Z

**Generated:** 2026-05-19T19:36:50Z
**Backlog snapshot:** 2026-05-19T19:31:17Z (after firewallanalyzer aggregator splits via PR #850)
**Schema version:** 3 (prepare-extended)
**Initiative count:** 15 (11 P2 + 4 P3, all child rows from the 2026-05-16 firewallanalyzer audit)

## Selection notes

count=15 cap requested; 23 sweep-actionable rows available after the gh #768 + gh #769 split (PR #850 landed 23 child rows replacing 2 aggregators).

**Selected (15):**
- All 11 P2 (Medium) — orders 1-11, covering audit sections 13.3 and 13.9-13.18
- Top 4 P3 (Low) — orders 12-15, picked by correctness/blast-radius/feasibility ranking:
  - Order 12: `remove-preview-family-invalidoperation-for-missing-items` (4-tool response-shape contract — highest blast radius among P3)
  - Order 13: `document-symbols-vs-symbol-info-record-kind-disagreement` (cross-tool semantic consistency)
  - Order 14: `get-msbuild-properties-vs-workspace-reload-outputtype-mismatch` (cross-tool consistency)
  - Order 15: `source-file-lines-off-by-one-marker-count` (off-by-one inconsistency between resource marker and canonical tool)

**Deferred (8 P3, will be picked up in next sweep):** `find-duplicate-helpers-framework-wrapper-filter-leak`, `compile-check-file-filter-scope-narrowing-inconsistency`, `get-nuget-dependencies-summary-cpm-literal`, `get-cohesion-metrics-always-null-lifecycle-pattern`, `add-pragma-suppression-crlf-in-lf-file`, `find-type-mutations-error-template-diverges-from-siblings`, `dependency-inversion-preview-newline-before-comma-formatting`, `go-to-definition-off-identifier-misleading-message`.

**Excluded:** 4 Reserved good-first-issue rows (skipped per planner Step 1 hard-skip), 1 track-only weak-evidence row (`tool-surface-pagination-or-tool-sets`), 6 Defer rows.

No Rule 1 bundle candidates identified at selection time — each child row addresses a distinct tool/service. The deepener may surface bundle candidates during anchor verification.

**Parent-tracker discipline:** every child row has plain-text references to gh #768 (P2) and gh #769 (P3) — no `[gh #NNN]` bracket-link. This means initiative-executor will NOT inject `Fixes #768` / `Fixes #769` on child PR bodies, so the parent trackers stay open until all children ship. Confirmed by grep against the post-#850 backlog.

## Initiatives

### 1. find-references-static-extension-host-blind-spot

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 2. code-fix-preview-vs-fix-all-preview-shape-inconsistency

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 3. suggest-refactorings-facade-extraction-false-positive

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 4. find-duplicated-methods-symmetric-mapper-false-positives

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 5. test-coverage-fail-fast-on-missing-coverlet

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 6. set-conditional-property-preview-allowlist-narrowness

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 7. get-completions-filtertext-doesnt-promote-in-scope-members

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 8. semantic-grep-dotted-identifiers-tokenization-docs-gap

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 9. get-di-registrations-multi-registration-overcounting

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 10. migrate-package-preview-no-op-silent-mutation

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 11. scaffold-first-test-file-preview-single-target-heuristic

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 12. remove-preview-family-invalidoperation-for-missing-items

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 13. document-symbols-vs-symbol-info-record-kind-disagreement

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 14. get-msbuild-properties-vs-workspace-reload-outputtype-mismatch

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

### 15. source-file-lines-off-by-one-marker-count

_Stanza pending — `plan-deepener` will fill in this section in Phase B._

## Conflict graph

_Pending — Phase C computes after Phase B._

## Review

_Pending — Phase D runs after Phase C._
