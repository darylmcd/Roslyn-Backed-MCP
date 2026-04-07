# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-07T12:00:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add “Completed” tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

| id | blocker | deps | do |
|----|---------|------|-----|
| `testbase-srp-split` | none | — | **P3 / refactor.** `TestBase` is a 318-line god class owning: 40+ static service properties, MSBuild init, filesystem helpers, path resolution, fixture copy logic, and now workspace caching. The recent fix made it idempotent and added a workspace cache, but the class still violates SRP. Plan: extract `TestServiceContainer` (services + lifecycle), `TestFixtures` (path resolution + copy helpers), and keep `TestBase` as a thin pass-through. Defer until a touch-the-class change is needed — the current split would touch 30+ test classes. |
| `vuln-scan-network-mock` | none | — | **P3 / test isolation.** `ScanNuGetVulnerabilitiesAsync_ReturnsStructuredResult` shells out to `dotnet list package --vulnerable`, which hits `api.nuget.org`. Now passes reliably with the workspace-cache fix, but still fails in air-gapped environments. Consider mocking the NuGet client at the gated-executor boundary, or marking the test `[Category("Network")]` so it can be filtered. |
| `verify-release-test-output-policy` | none | — | **P3 / process.** `eng/verify-release.ps1` runs `dotnet test` with default verbosity. Add `--logger "console;verbosity=normal"` and `$ErrorActionPreference = 'Stop'` so failing tests are not masked by tail/pipe operations in any wrapper script. |
| `semantic-search-async-modifier-doc` | none | — | **P3 / docs.** `semantic_search` query `async methods returning Task<bool>` returns 0 because Roslyn `IMethodSymbol.IsAsync` requires the `async` modifier on the declaration; pass-through `Task.FromResult` methods are not matched. Document this gotcha in `CodePatternAnalyzer` tool description and the `/roslyn-mcp:review` skill so users know to query for "methods returning Task<bool>" without "async" if they want all Task-returning methods. |
| `workspace-rw-lock-flag-flip` | one full release of opt-in soak | `workspace-rw-lock` (shipped opt-in) | **P2 / perf rollout.** Per-workspace `AsyncReaderWriterLock` ships behind `ROSLYNMCP_WORKSPACE_RW_LOCK=true`, default off. Phase-2 follow-up: flip the default to `true`, drop the env-var binding from `Program.BindExecutionGateOptions`, delete the legacy `_workspaceGates`/`_useRwLock` branch in `WorkspaceExecutionGate`, remove the `RunAsync` compat shim if all callers have migrated, and remove this row. Drives the read-heavy unlock from the deep-review survey. Tracked in `ai_docs/reports/2026-04-06-workspace-rw-lock-design-note.md`. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
