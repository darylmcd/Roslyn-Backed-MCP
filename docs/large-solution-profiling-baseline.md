# Large-solution profiling baseline

This document implements **Phase 6** of the release-hardening plan: a **repeatable method** to decide whether to invest in indexing/caching (`docs/roadmap.md` — Large-Solution Performance Strategy) versus staying on hardening.

## Purpose

- Establish **P50 / P95 / P99** wall-clock times for representative operations on a **large** solution (target: 50+ projects, or your worst-case customer repo).
- If P95 is acceptable for single-symbol and common scans, **defer** performance-engineering until metrics justify it.

## Prerequisites

- Same machine class as typical developers (or document CPU/RAM/disk).
- Cold vs warm: document whether the workspace was just loaded or reused.
- `ROSLYNMCP_*` env vars at default unless testing specific caps (`ai_docs/runtime.md`).

## Operations to measure

| Operation | Tool / API | Notes |
|-----------|------------|--------|
| Workspace load | `workspace_load` | End-to-end open + first `workspace_status`. |
| Symbol search | `symbol_search` | Bounded query; record limit and pattern. |
| Find references | `find_references` | Single symbol with typical fan-out. |
| Compile check | `compile_check` | `emitValidation: false` vs `true` separately. |
| Build | `build_project` or full solution | As used in practice. |
| Test run | `test_run` | Smallest test project if full suite is too slow. |

## Procedure

1. Load workspace once; discard first timing if including JIT/warmup (or report separately).
2. For each operation, run **at least 5** iterations; record milliseconds.
3. Compute P50 / P95 / P99 (spreadsheet or script).
4. Record solution stats: project count, document count, approximate LOC if available.

## Decision gate (example thresholds)

| Signal | Stay on hardening | Open performance epic |
|--------|-------------------|-------------------------|
| Single-symbol navigation (`go_to_definition`, `find_references`) P95 | &lt; 5 s | &gt; 5 s consistently |
| Solution-wide search P95 | &lt; 30 s | &gt; 30 s on representative queries |
| Workspace load P95 | &lt; 2 min | &gt; 2 min blocking daily use |

Thresholds are **examples** — adjust for your user expectations.

## Result template (fill per run)

| Field | Value |
|-------|--------|
| Date | |
| Solution path (describe only; no secrets) | |
| Project count | |
| roslyn-mcp version / commit | |
| workspace_load P50 / P95 | |
| symbol_search P50 / P95 | |
| find_references P50 / P95 | |
| compile_check (no emit) P50 / P95 | |
| compile_check (emit) P50 / P95 | |
| Decision | Stay on hardening / Open performance backlog item |

## Backlog linkage

If performance work is justified, add a row to `ai_docs/backlog.md` with **measured** P95, operation name, and solution profile — not generic “make it faster.”

## Related

- `docs/parity-gap-implementation-plan.md` — hardening vs performance recommendation
- `docs/roadmap.md` — indexing/caching direction
