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

## Automation

Use `eng/profile-large-solution.ps1` to run the repeatable MCP timing pass. The
default target is `C:\Code-Repo\OrchardCore\OrchardCore.slnx`, a local 50+ project
representative solution when present. See
`ai_docs/prompts/profile-large-solution.md` for the runbook and interpretation steps.

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

## Recorded run — OrchardCore large solution (2026-04-26)

This run is the first representative 50+ project profile for the
`workspace-process-pool-or-daemon` evidence gate. It used the local OrchardCore
checkout and wrote ignored raw artifacts under
`artifacts/large-solution-profiling/20260426T212443Z/`.

| Field | Value |
|-------|--------|
| Date | 2026-04-26T21:29:37Z |
| Solution | `C:\Code-Repo\OrchardCore\OrchardCore.slnx` |
| OrchardCore git | `main` / `f852c64` |
| Project count | 227 solution projects; 233 repo projects |
| Tracked C# files | 5,190 |
| roslyn-mcp version / commit | `1.33.0+7b28ea43144f998dad6570efa7782bc1712ada10` |
| Method | `eng/profile-large-solution.ps1 -SolutionPath C:\Code-Repo\OrchardCore\OrchardCore.slnx -Iterations 5 -SymbolQuery ContentItem`; included upfront `dotnet restore`; did not run emit validation |
| Selected reference symbol | `OrchardCore.Alias.Indexes.AliasPartIndex.ContentItemId` |
| workspace_load P50 / P95 | 40,629.54 ms / 44,849.03 ms |
| symbol_search P50 / P95 | 74 ms / 1,177.84 ms |
| find_references P50 / P95 | 5.44 ms / 996.87 ms |
| workspace_warm P50 / P95 | 1.31 ms / 17,321.76 ms |
| compile_check (no emit) P50 / P95 | 420.41 ms / 7,178.45 ms |
| Failures | None. `compile_check_no_emit` returned 5 target-solution errors every iteration, so the timing is successful but not a clean-solution compile baseline. `workspace_load` summaries reported `restoreRequired=true` despite the upfront restore succeeding. |
| Decision | **Stay on hardening.** Keep `workspace-process-pool-or-daemon` deferred: workspace-load P95 is below the 2 minute threshold, and navigation/search P95 values are below the documented thresholds. |

## Recorded run — sample solution smoke (2026-04-04)

This run **validates the procedure** on the repo’s small sample fixture. It is **not** a substitute for measuring a 50+ project “worst case” solution; treat it as a methodology checkpoint only.

| Field | Value |
|-------|--------|
| Date | 2026-04-04 |
| Solution | Repository `samples/SampleSolution` (fixture for integration tests) |
| Project count | 4 |
| roslyn-mcp version / commit | 1.6.0 (see `Directory.Build.props`) |
| Method | Wall-clock budgets enforced by `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs` (`dotnet test RoslynMcp.slnx --filter PerformanceBaselineTests`) — not full P50/P95 spreadsheet |
| workspace_load / symbol_search / find_references | All completed within the generous budgets in `PerformanceBaselineTests` on a typical dev workstation |
| compile_check | Covered by `ValidationToolsIntegrationTests` and `HighValueCoverageIntegrationTests` (correctness + failure fixture); not timed for P95 here |
| Decision | **Stay on hardening** for this profile. **Large-solution (50+ projects) profiling:** repeat the template above when a representative customer-scale solution is available; add a backlog row only with measured P95 per `## Backlog linkage`. |

## Backlog linkage

If performance work is justified, add a row to `ai_docs/backlog.md` with **measured** P95, operation name, and solution profile — not generic “make it faster.”

## Related

- `docs/parity-gap-implementation-plan.md` — hardening vs performance recommendation
- `docs/roadmap.md` — indexing/caching direction
