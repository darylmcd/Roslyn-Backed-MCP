# Backlog sweep plan — 20260620T215409Z

**Generated:** 2026-06-20T21:54:09Z
**Backlog snapshot:** 2026-06-20T04:18:39Z
**Initiative count:** 1
**Anchor verification:** performed (orchestrator pre-derived sites; deepener re-verifies)

> **Scope note (operator-directed):** This plan is a deliberately bounded LOW-RISK subset of the open row `compilation-cache-adoption-read-side`. Re-derivation found that row's broader scope is much larger than its anchors (48 raw `GetCompilationAsync` sites / 30 files, with safety-critical forked-solution exclusions). The operator chose to sweep only the cleanest live-solution reads (group-a core) now; the row stays OPEN for the remaining a-tail / group-b / group-c (hazard) work.

<!-- BSWEEP:STATUS-TABLE BEGIN — generated from state.json; do not edit by hand -->
## Status (generated)

| # | id | status | PR | rows closed |
|---|----|--------|----|-------------|
| 1 | compcache-batch-a-core-reference-reads | pending | — | — |
<!-- BSWEEP:STATUS-TABLE END -->

## Initiatives (in order)

### 1. compcache-batch-a-core-reference-reads

| Field | Content |
|---|---|
| Diagnosis | _(deepener fills)_ |
