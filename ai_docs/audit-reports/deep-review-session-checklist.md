# Deep review & MCP audit — session checklist

<!-- purpose: Fill-in worksheet for live deep-review runs; keep phase order aligned with prompts/deep-review-and-refactor.md. -->

Use with [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md). A full PASS/FAIL matrix needs an **interactive MCP session** (e.g. Cursor) with the server running (`dotnet run --project src/RoslynMcp.Host.Stdio` or global `roslynmcp`).

**Canonical output:** raw evidence goes to `yyyyMMddTHHmmssZ_<repo-id>_mcp-server-audit.md` in this directory (see [`README.md`](README.md)). This file is only a **worksheet** — not a substitute for that report.

## Session parameters

| Field | Value |
|-------|--------|
| Target solution | Default: `samples/SampleSolution/` — use a richer repo when needed |
| Server version | From `server_info` at session start |
| Catalog version | From `server_info.catalogVersion` (e.g. `2026.04`) |
| Host | stdio (`RoslynMcp.Host.Stdio`) |
| Audit mode | `full-surface` by default; record reason if `conservative` |
| Isolation | Disposable branch/worktree/clone path, or conservative rationale |
| Prior issue source | `ai_docs/backlog.md` when available; otherwise previous audit or issue tracker |
| Client capability limits | Note whether this MCP client can invoke prompts and read resources directly |
| Debug log channel | `yes` / `partial` / `no` — whether the client surfaces MCP `notifications/message` log entries |
| Plugin skills repo path | Absolute/relative path to the Roslyn-Backed-MCP checkout used for Phase 16b (or `blocked — <reason>`) |

## Automated pre-checks (repository)

Runs without MCP:

- `./eng/verify-ai-docs.ps1`
- `./eng/verify-release.ps1 -Configuration Release` (build, tests with coverage, publish, manifests)
- `SurfaceCatalogTests` — catalog matches registered tools/resources/prompts

## Audit sections (fill during MCP session)

### 1. Setup, repo shape, and client limits

Capture Phase 0 facts before the audit gets noisy.

| Item | Notes |
|------|-------|
| Loaded entrypoint (`.sln` / `.slnx` / `.csproj`) | |
| `full-surface` vs `conservative` | |
| Isolation path or conservative rationale | |
| Repo shape (projects, tests, analyzers, DI, source generators, CPM, multi-targeting, network limits) | |
| Client can read resources? | yes / no + notes |
| Client can invoke prompts? | yes / no + notes |
| Plugin skills repo reachable for Phase 16b? | yes / no + reason |

### 2. Coverage closure and ledger discipline

Track the live catalog, not the client UI. The ledger carries `tier` and `lastElapsedMs` columns in v2 — seed them in Phase 0.

| Check | Status | Notes |
|------|--------|-------|
| `server_info` counts match `roslyn://server/catalog` | | |
| Initial coverage ledger seeded with every live tool/resource/prompt (including `tier` + `category` columns) | | |
| Every final row has one status (`exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, `blocked`) | | |
| `blocked` rows identify client limit vs workspace-load failure vs server/runtime fault | | |
| Final ledger totals match live catalog totals | | |
| Promotion scorecard seeded (one row per experimental entry) | | |

### 3. Phase matrix

Order matches the living prompt (**8b** after **8**; **9** after **10**; **16b** after **16**). Record families exercised, status mix, and **PASS / FLAG / FAIL**.

| Phase | Families exercised | PASS / FLAG / FAIL | Notes |
|------|--------------------|--------------------|-------|
| 0 | | | |
| 1 | | | |
| 2 | | | |
| 3 | | | |
| 4 | | | |
| 5 | | | |
| 6 | | | |
| 7 | | | |
| 8 | | | |
| 8b | | | Concurrency / RW lock |
| 10 | | | |
| 9 | | | Undo (after Phase 10) |
| 11 | | | |
| 12 | | | |
| 13 | | | |
| 14 | | | |
| 15 | | | |
| 16 | | | Prompt verification |
| 16b | | | Plugin skills audit |
| 17 | | | |
| 18 | | | |

### 4. Parameter-path coverage

At least one non-default selector/flag path per major family when the live schema exposes it. Cross-cutting principle #6 → report section 9.

| Family / Tool | Non-default path tested | Result | Notes |
|------|--------------------------|--------|-------|
| Diagnostics | project / file / severity / paging | | |
| `compile_check` | `emitValidation=true` | | |
| Analyzer listing | paging / project filter if exposed | | |
| Search / analysis families | filter / limit / alternate kind if exposed | | |
| Prompt / resource families | client-blocked vs exercised | | |

### 5. Schema vs behaviour drift

Cross-cutting principle #2 → report section 7.

| Tool | Mismatch kind | Expected | Actual | Severity |
|------|---------------|----------|--------|----------|
| *(fill)* | | | | |

### 6. Performance baseline (`_meta.elapsedMs`)

Cross-cutting principle #3 → report section 6. One row per exercised tool.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | Budget status |
|------|------|----------|-------|--------|--------|----------------|
| *(fill)* | | | | | | |

### 7. Error message quality and negative-path checks

Cross-cutting principle #4 → report section 8.

| Tool | Probe input | Rating (actionable / vague / unhelpful) | Notes |
|------|-------------|------------------------------------------|-------|
| *(fill)* | | | |

Mutation-safety and invalid-input checks:

| Check | Status | Notes |
|------|--------|-------|
| Applied preview token rejected on second apply | | |
| Older preview token rejected after workspace version advances | | |
| `revert_last_apply` second call reports nothing to undo clearly | | |
| Post-close workspace-scoped call fails clearly | | |

### 8. Response contract consistency

Cross-cutting principle #5 → report section 18. Note 0-based vs 1-based lines, field naming across related tools, classification enum drift, etc. Leave empty when no inconsistencies observed.

### 9. Prompt verification (Phase 16)

Report section 10. One row per exercised prompt.

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | recommendation_seed |
|--------|-----------|------------|--------------------|------------|----------------------|
| *(fill for every exercised prompt)* | | | | | |

### 10. Plugin skills audit (Phase 16b)

Report section 11. One row per live skill (2026.04 snapshot: 11 skills). Mark `blocked` with a reason if plugin repo not reachable.

| Skill | frontmatter_ok | tool_refs_valid (invalid_count) | dry_run | safety_rules |
|-------|----------------|----------------------------------|---------|--------------|
| `analyze` | | | | na |
| `complexity` | | | | na |
| `dead-code` | | | | |
| `document` | | | | |
| `explain-error` | | | | |
| `migrate-package` | | | | |
| `refactor` | | | | |
| `review` | | | | na |
| `security` | | | | na |
| `test-coverage` | | | | |

### 11. Experimental promotion scorecard

Report section 12. Four-way recommendation per experimental entry. Rubric: see prompt's **Final surface closure → step 8**.

| Kind | Name | Recommendation | Evidence |
|------|------|-----------------|----------|
| tool | *(fill every experimental tool)* | `promote` / `keep-experimental` / `needs-more-evidence` / `deprecate` | |
| resource | *(not applicable — all resources are stable)* | n/a | |
| prompt | *(fill every prompt — all currently experimental)* | | |

### 12. Phase 6 refactor summary and cleanup verification

| Check | Status | Notes |
|------|--------|-------|
| Phase 6 applies summarized | | |
| Audit-only mutations from Phases 8b, 9–13 cleaned up or reverted | | |
| Only intentional Phase 6 product changes remain | | |

## Findings routed to backlog

| id | Summary | Severity |
|----|---------|----------|
| *(none yet)* | File new rows in `ai_docs/backlog.md` for FAIL-grade defects. | |

## Maintainer note (optional)

If this repo gained integration tests or CI changes in the same effort as an audit, note them here briefly. **Raw per-run evidence** still belongs in a timestamped `*_mcp-server-audit.md` file.
