# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-08T23:40:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add "Completed" tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

Ordered by severity: P2 (contract violations / real-world blockers) → P3 (refactors, accuracy, UX) → P4 (docs + cosmetic).

| id | blocker | deps | do |
|----|---------|------|-----|
| `verify-release-test-output-policy` | none | — | **P4 / process.** `eng/verify-release.ps1` already sets `$ErrorActionPreference = 'Stop'`, but still runs `dotnet test` with default verbosity. Add `--logger "console;verbosity=normal"` so failing test names are not masked by tail/pipe operations in any wrapper script. |
| `compile-items-vs-document-count-doc` | none | — | **P4 / docs.** `evaluate_msbuild_items Compile <project>` returns N items but Roslyn `workspace_load` reports `DocumentCount=N+3` for the same project (observed in the 2026-04-07 FirewallAnalyzer audit: `FirewallAnalyzer.Application` had 17 Compile items vs 20 documents). The 3-item gap is normal — auto-generated implicit-usings, assembly-info, and GlobalUsings files are added by the SDK and aren't in the explicit Compile item list. Document this expectation in the `workspace_load` and `evaluate_msbuild_items` tool descriptions so downstream agents can interpret the difference. |
| `semantic-search-async-modifier-doc` | none | — | **P4 / docs.** `semantic_search` query `async methods returning Task<bool>` returns 0 because Roslyn `IMethodSymbol.IsAsync` requires the `async` modifier on the declaration; pass-through `Task.FromResult` methods are not matched. Document this gotcha in `CodePatternAnalyzer` tool description and the `/roslyn-mcp:review` skill so users know to query for "methods returning Task<bool>" without "async" if they want all Task-returning methods. |
| `tool-substring-matching-docs` | none | — | **P4 / docs.** `get_msbuild_properties`'s `propertyNameFilter` already documents "case-insensitive substring filter", but `symbol_search`'s `query` description only says "supports partial matching" — ambiguous about whether it's prefix, substring, or fuzzy. Update `symbol_search` to explicitly say "substring match (case-insensitive)" so callers know to pass bare fragments instead of wildcard patterns. |
| `server-info-prompts-tier-doc` | none | — | **P4 / docs.** `server_info` reports `prompts: stable=0, experimental=16` but the live catalog only exposes 16 experimental prompts — it is unclear whether prompts have a stable tier at all. Confirm the tiering convention is intentional and document it in the `server_info` tool description (or rename the field if prompts don't have tiers). |
| `compile-check-vs-analyzers-doc` | none | — | **P4 / docs.** `compile_check` can report zero CS diagnostics while `project_diagnostics` lists many CA/IDE warnings on the same workspace — correct by design but confusing (2026-04-08 ITChatBot audit). Clarify CS-only vs analyzer-inclusive semantics in both tool descriptions. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
