# Deep review & MCP audit — baseline session (2026-04-04)

This file is a **baseline template** for executing [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md). A full PASS/FAIL matrix requires an **interactive MCP session** (Cursor or other client) with the server running (`dotnet run --project src/RoslynMcp.Host.Stdio` or global `roslynmcp`).

## Session parameters

| Field | Value |
|-------|--------|
| Target solution | Default: `samples/SampleSolution/` — substitute a richer repo if needed |
| Server version | Assembly version from `server_info` at session start |
| Host | stdio (`RoslynMcp.Host.Stdio`) |

## Automated pre-checks (repository)

The following run in CI and locally without MCP:

- `./eng/verify-ai-docs.ps1`
- `./eng/verify-release.ps1 -Configuration Release` (build, tests with coverage, publish, manifests)
- `SurfaceCatalogTests` — catalog matches registered tools/resources/prompts

## Audit sections (fill during MCP session)

### 1. Tool coverage matrix

For each phase in the deep-review prompt, record tools **called** vs **skipped** and a **PASS / FLAG / FAIL** tag.

### 2. Schema vs behavior

| Tool | Schema accurate? | Notes |
|------|------------------|-------|
| *(fill)* | | |

### 3. Performance observations

| Tool | Approx. wall time | Input scale |
|------|-------------------|-------------|
| *(fill)* | | |

### 4. Error message quality

| Tool | Actionable / Vague / Unhelpful | Notes |
|------|-------------------------------|-------|
| *(fill)* | | |

### 5. Response contract consistency

Note 0-based vs 1-based lines, field naming across related tools, etc.

### 6. Phase 6 refactor summary

If refactors were applied in the **target** repo during the audit, list commits/files here.

## Findings routed to backlog

| id | Summary | Severity |
|----|---------|----------|
| *(none yet)* | File new rows in `ai_docs/backlog.md` for FAIL-grade defects. | |

## Safe refactors applied in **this** repo (roslyn-mcp)

During implementation of the release-hardening plan (same date), no separate deep-review MCP session was executed in this workspace; **integration tests** for `compile_check` were added in `ValidationToolsIntegrationTests.cs`, and **coverage** was wired into `verify-release.ps1`. Re-run this audit file with a live MCP session when validating a release candidate.
