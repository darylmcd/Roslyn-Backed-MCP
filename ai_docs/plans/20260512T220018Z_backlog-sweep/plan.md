# Backlog sweep plan — 20260512T220018Z

**Generated:** 2026-05-12T22:00:18Z
**Backlog snapshot:** 2026-05-12T21:20:00Z
**Initiative count:** 5
**Anchor verification:** pending

## Selection rationale

All Critical, High, and Medium sections are empty. 7 of 13 Low rows are Reserved (contributor-pickup) and excluded from sweep. 1 row (`tool-surface-pagination-or-tool-sets`) is explicitly "Track only; not yet actionable" (neither trigger condition met — surface count 167, below 200 threshold; no small-model friction report) and excluded. All 5 Defer rows are excluded.

**Skipped rows:**

- `add-project-reference-self-reference-not-rejected` — Reserved gh #608
- `compile-check-project-filter-miss-no-error-envelope` — Reserved gh #609
- `find-duplicated-methods-mcp-wrapper-false-positive` — Reserved gh #612
- `get-prompt-text-multi-step-required-param-errors` — Reserved gh #610
- `goto-type-definition-builtins-invalidoperation` — Reserved gh #607
- `test-run-unfiltered-no-failure-envelope` — Reserved gh #611
- `workspace-load-prewarm-double-nullable` — Reserved gh #606
- `tool-surface-pagination-or-tool-sets` — "Track only" self-exclusion (neither activation criterion met)
- Defer section (5 rows): `scorecard-blocked-to-backlog-row`, `validate-locator-preflight-tool`, `http-streamable-host-project`, `workspace-process-pool-or-daemon`, `workspace-manager-cache-store-extraction`

**5 rows selected** (all Low priority — this backlog has no P2/P3 open rows):

| Order | Row id | Rationale |
|---|---|---|
| 1 | `compile-check-not-connected-raw-transport-error-envelope` | Correctness — transport-layer error missing structured envelope; concrete symptom + fix path |
| 2 | `list-analyzers-totalrules-variance` | UX-correctness — misleading field name or non-deterministic count; small scope |
| 3 | `workspace-staleness-cross-workspace-contamination` | Performance/correctness — cross-workspace stale-reload; concrete investigation path |
| 4 | `scaffolding-service-split-by-scaffold-type` | Organizational refactor — Rule 3 scoping TBD by deepener |
| 5 | `skill-namespace-installed-as-bulk-frontmatter-migration` | Bulk frontmatter update — Rule 3 scoping REQUIRED (46 files; deepener must wave-1 scope) |

**Rule 1 bundle candidates:** none identified (no rows share the same code path).

---

## Initiatives (in order)

### 1. compile-check-not-connected-raw-transport-error-envelope

*(deepener stanza pending)*

### 2. list-analyzers-totalrules-variance

*(deepener stanza pending)*

### 3. workspace-staleness-cross-workspace-contamination

*(deepener stanza pending)*

### 4. scaffolding-service-split-by-scaffold-type

*(deepener stanza pending)*

### 5. skill-namespace-installed-as-bulk-frontmatter-migration

*(deepener stanza pending)*
