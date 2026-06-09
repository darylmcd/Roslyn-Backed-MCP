# Backlog sweep plan — 20260609T134405Z

**Scope:** the 3 `workspace-auto-load-on-demand` feature rows (operator-scoped). Design spec: `ai_docs/items/workspace-auto-load-on-demand-design.md`.

**Sequencing:** strict dependency chain `1 → 2 → 3`. All three touch `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`, so they are **fully serial** (no parallel wave). Initiative 3 is the catalog **hotspot** (`ServerSurfaceCatalog.*`). Orders 2–3 partly anchor on code order 1 introduces — see per-stanza notes.

<!-- BSWEEP:STATUS-TABLE -->

---

### 1. workspace-id-omitted-single-resolve

_(deepener pass pending)_

### 2. workspace-auto-load-on-demand

_(deepener pass pending)_

### 3. workspace-id-optional-readonly-surface-flip

_(deepener pass pending)_

<!-- BSWEEP:STATUS-TABLE BEGIN — generated from state.json; do not edit by hand -->
## Status (generated)

| # | id | status | PR | rows closed |
|---|----|--------|----|-------------|
| 1 | workspace-id-omitted-single-resolve | pending | — | workspace-id-omitted-single-resolve |
| 2 | workspace-auto-load-on-demand | pending | — | workspace-auto-load-on-demand |
| 3 | workspace-id-optional-readonly-surface-flip | pending | — | workspace-id-optional-readonly-surface-flip |
<!-- BSWEEP:STATUS-TABLE END -->
