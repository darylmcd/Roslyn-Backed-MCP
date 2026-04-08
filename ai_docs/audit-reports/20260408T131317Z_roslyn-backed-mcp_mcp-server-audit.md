# MCP Server Audit Report

**Run type:** Second abbreviated pass of `ai_docs/prompts/deep-review-and-refactor.md` (2026-04-08). Full phased closure, per-phase `.draft.md` streaming, and exercising every catalog tool are **not** claimed; see **Coverage** and **Completion** below.

## Header

| Field | Value |
|-------|--------|
| **Date** | 2026-04-08 (UTC ~13:13Z) |
| **Audited solution** | `c:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln` |
| **Revision** | `7c9fe00` (`main`; working tree may include local edits — see Phase 6) |
| **Audit mode** | `full-surface` intent; **isolation** same working tree (no disposable worktree) |
| **Client** | Cursor agent + `user-roslyn` MCP |
| **Workspace id** | `1915c1979f344be6b85304cfc82198d7` (closed at end of run) |
| **Server** | `roslyn-mcp` **1.7.0+46e9f728**; **catalogVersion** `2026.03` |
| **Roslyn / runtime** | Roslyn **5.3.0.0**; **.NET 10.0.5**; OS **Windows 10.0.26200** |
| **Scale** | 6 projects, 321 documents |
| **Repo shape** | Multi-project; tests in `RoslynMcp.Tests`; CPM; `net10.0`; `.editorconfig` present |
| **Prior issue source** | `ai_docs/backlog.md` |
| **Debug log channel** | `no` (MCP log notifications not surfaced to the agent) |
| **Draft persistence** | Partial: `20260408T131317Z_roslyn-backed-mcp_mcp-server-audit.draft.md` seeded mid-run; finalized in this canonical file |

## Coverage summary

| Kind | Exercised this run | Blocked / skipped |
|------|--------------------|-------------------|
| Tools (subset) | `server_info`, `workspace_load`, `workspace_list`, `project_diagnostics`, `compile_check`, `build_workspace`, `test_run` (failed), `workspace_close` | Remaining ~115 tools not called → **`skipped-session`** |
| Resources | `roslyn://workspaces` | Other workspace URIs not compared to tools |
| Prompts | 0 | **16** — `blocked` (no prompt invocation in this client) |

**Catalog agreement:** `server_info` surface counts match `roslyn://server/catalog` (**123** tools, **7** resources, **16** prompts).

## Evidence (this run)

1. **`workspace_list`:** **`count: 1`** after a single `workspace_load` — no duplicate session for the same solution in this host session (contrasts with prior run where `count: 2`).
2. **`project_diagnostics` / `compile_check`:** One compiler warning **CS0414** on intentional `DiagnosticsProbe` in SampleLib; counts aligned between tools.
3. **`build_workspace`:** `ExitCode: 0`, `Succeeded: true`, `DurationMs` ~2034.
4. **`test_run` (MCP):** Invocation **failed** (`An error occurred invoking 'test_run'.`) — no structured payload; **severity:** client/tool bridge or server exception not exposed.
5. **Shell verification:** `dotnet test RoslynMcp.Tests.csproj --no-build --filter "FullyQualifiedName~BacklogFixTests"` → **Passed: 14**, Failed: 0 (duration ~3 s).
6. **Resource `roslyn://workspaces`:** JSON matches single active workspace before `workspace_close`.

## Phase 6 refactor summary

| Item | Detail |
|------|--------|
| **This run** | **No** new Roslyn apply (`rename`, `fix_all`, `organize_usings`, etc.). |
| **Working tree** | Prior session may have modified `Program.cs` (import order); not re-applied here. |
| **Verification** | `build_workspace` succeeded; subset `dotnet test` passed. |

## MCP server issues

### 1. `test_run` MCP invocation failure

| Field | Detail |
|--------|--------|
| Tool | `test_run` |
| Input | `workspaceId`, `projectName`: `RoslynMcp.Tests` |
| Expected | Structured test results or clear server error body |
| Actual | Generic failure: `An error occurred invoking 'test_run'.` |
| Severity | **missing data** / **error-message-quality** |
| Reproducibility | observed once this run; prior run had different failure (file locks) |

### 2. (Backlog cross-check) `project_diagnostics` filter totals

Not re-probed this run; remains **`project-diagnostics-filter-totals`** in `ai_docs/backlog.md`.

## Performance observations

| Tool | Approx wall-clock | Notes |
|------|-------------------|--------|
| `project_diagnostics` (full) | ~6970 ms `_meta.heldMs` | Solution-wide |
| `compile_check` | ~2440 ms | 6 projects |
| `build_workspace` | ~2044 ms | `dotnet build` wrapper |

## Known issue regression check

| id | Result |
|----|--------|
| Duplicate `workspace_list` entries | **Not reproduced** this run (`count: 1`) — prior duplicate may be host/session dependent |
| `test_run` reliability | **Still problematic** — different failure mode than file-lock run |

## Improvement suggestions

- Return **actionable JSON** when `test_run` fails (exit code, first lines of stdout/stderr) instead of a generic invocation error.
- Document whether **`test_run`** is expected to be flaky under concurrent `dotnet` / locked binaries on Windows.

## Completion gate

| Requirement | Status |
|---------------|--------|
| Canonical report path under `ai_docs/audit-reports/` | **Yes** — this file |
| Full deep-review prompt (all phases, full ledger rows) | **No** — **partial** audit; expand with companion CLI or multi-session per backlog |

---

*Workspace `1915c1979f344be6b85304cfc82198d7` closed via `workspace_close` after data collection.*
