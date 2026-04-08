# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-08T18:05:00Z

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
| `test-run-failure-envelope` | none | — | **P2 / reliability.** 2026-04-08 audits: `test_run` failed with MSB3027/3021 file locks when `testhost` still holds Roslyn DLLs (roslyn-backed-mcp 130615), and with a generic MCP bridge error `An error occurred invoking 'test_run'` with no structured payload (131317). Surface exit code, stdout/stderr excerpts, and whether the failure is retryable; document Windows concurrent-test behavior in the tool description. |
| `audit-feasibility-companion-cli` | none | `workspace-payload-summary-modes` | **P2 / tooling.** A complete walkthrough of `ai_docs/prompts/deep-review-and-refactor.md` against a 34-project / 751-document solution does not fit in a single Claude Code conversation today, primarily because workspace-payload bloat tools (`workspace_load`, `workspace_status`, `workspace_list`, `roslyn://workspaces`, `get_nuget_dependencies`, `list_analyzers`, `get_msbuild_properties`, `find_unused_symbols`) exhaust the per-turn ~250 KB output budget by mid-prompt — observed against the 34-project ITChatBot.sln in the 2026-04-07 rw-lock audit. Consider shipping a `roslyn-mcp-audit` companion CLI that drives the deep-review prompt headlessly and emits the canonical raw audit file as an artifact, freeing the LLM from having to render every tool result back through context. The payload-summary-modes fix alone may lift this ceiling enough that a CLI is unnecessary — re-evaluate after that lands. |
| `reader-tool-elapsed-ms` | none | — | **P3 / instrumentation.** Reader-family tools (`find_references`, `symbol_search`, `find_unused_symbols`, `get_complexity_metrics`, `get_cohesion_metrics`, etc.) do not surface `ElapsedMs` in their responses — only `compile_check`, `build_project`, `test_run`, `evaluate_csharp`, and `nuget_vulnerability_scan` emit timing. Result: agents cannot fill the deep-review prompt's Phase 8b sequential-baseline / parallel-fan-out concurrency matrix with quantitative measurements from inside the agent loop, so concurrency audits produce only behavioral evidence (per the 2026-04-07 FirewallAnalyzer audit). Add an optional `ElapsedMs` field to all reader responses so concurrency audits can produce numeric speedup ratios without external instrumentation. |
| `workspace-session-deduplication` | none | — | **P3 / session model.** Loading the same solution path twice in one host process can yield `workspace_list` with multiple distinct `WorkspaceId`s (2026-04-08 roslyn-backed-mcp audit 130615; not reproduced on a later pass — intermittent). Wastes workspace slots and confuses audits. Consider idempotent `workspace_load` or explicit dedup. |
| `revert-last-apply-disk-consistency` | none | — | **P3 / undo.** `revert_last_apply` reported success while the on-disk file still contained the applied text until manual cleanup and `workspace_reload` (2026-04-08 NetworkDocumentation audit). Align workspace model with disk or document the edge case. |
| `semantic-search-zero-results-verbose-query` | none | — | **P3 / UX.** `semantic_search` returned `count: 0` for a long natural-language query while a shorter query returned hits on the same repo (2026-04-08 FirewallAnalyzer audit). Add keyword/stem fallback or scoring/debug payload when the embedding ranker returns empty. |
| `vuln-scan-network-mock` | none | — | **P3 / test isolation.** `ScanNuGetVulnerabilitiesAsync_ReturnsStructuredResult` shells out to `dotnet list package --vulnerable`, which hits `api.nuget.org`. Passes reliably with the workspace-cache fix, but still fails in air-gapped environments. Consider mocking the NuGet client at the gated-executor boundary, or marking the test `[Category("Network")]` so it can be filtered. |
| `compile-check-emit-validation-regression-check` | none | — | **P3 / investigation.** The 2026-04-07 firewallanalyzer rw-lock audit reported `compile_check` with `emitValidation=true` returning byte-identical output (1 char shorter) to the default call, with no measurable wall-clock difference. The 2026-04-06 prior audit reported `emitValidation=true` was "~94× slower" with additional findings. Either (a) `emitValidation` regressed to a no-op when the base compilation has unresolved package references, or (b) the prior measurement was incidental. Re-test on a clean workspace with restored packages to determine which. If regressed, restore the emit path; if not, document the precondition in the tool description (currently `CompileCheckTools` only flags a generic perf caveat). |
| `find-type-usages-cref-classification` | none | — | **P4 / cosmetic.** `find_type_usages` buckets `<see cref="X"/>` doc-comment references as `Other` instead of as a `Documentation` classification (observed against ITChatBot 2026-04-07). Enrich the classification enum so doc-comment refs are visually distinguishable from real consumers. |
| `verify-release-test-output-policy` | none | — | **P4 / process.** `eng/verify-release.ps1` already sets `$ErrorActionPreference = 'Stop'`, but still runs `dotnet test` with default verbosity. Add `--logger "console;verbosity=normal"` so failing test names are not masked by tail/pipe operations in any wrapper script. |
| `compile-items-vs-document-count-doc` | none | — | **P4 / docs.** `evaluate_msbuild_items Compile <project>` returns N items but Roslyn `workspace_load` reports `DocumentCount=N+3` for the same project (observed in the 2026-04-07 FirewallAnalyzer audit: `FirewallAnalyzer.Application` had 17 Compile items vs 20 documents). The 3-item gap is normal — auto-generated implicit-usings, assembly-info, and GlobalUsings files are added by the SDK and aren't in the explicit Compile item list. Document this expectation in the `workspace_load` and `evaluate_msbuild_items` tool descriptions so downstream agents can interpret the difference. |
| `semantic-search-async-modifier-doc` | none | — | **P4 / docs.** `semantic_search` query `async methods returning Task<bool>` returns 0 because Roslyn `IMethodSymbol.IsAsync` requires the `async` modifier on the declaration; pass-through `Task.FromResult` methods are not matched. Document this gotcha in `CodePatternAnalyzer` tool description and the `/roslyn-mcp:review` skill so users know to query for "methods returning Task<bool>" without "async" if they want all Task-returning methods. |
| `tool-substring-matching-docs` | none | — | **P4 / docs.** `get_msbuild_properties`'s `propertyNameFilter` already documents "case-insensitive substring filter", but `symbol_search`'s `query` description only says "supports partial matching" — ambiguous about whether it's prefix, substring, or fuzzy. Update `symbol_search` to explicitly say "substring match (case-insensitive)" so callers know to pass bare fragments instead of wildcard patterns. |
| `server-info-prompts-tier-doc` | none | — | **P4 / docs.** `server_info` reports `prompts: stable=0, experimental=16` but the live catalog only exposes 16 experimental prompts — it is unclear whether prompts have a stable tier at all. Confirm the tiering convention is intentional and document it in the `server_info` tool description (or rename the field if prompts don't have tiers). |
| `analyze-snippet-cs0029-literal-span` | none | — | **P4 / diagnostics UX.** For embedded bad assignments in `analyze_snippet`, the CS0029 span may not highlight the string literal (2026-04-08 FirewallAnalyzer audit). Align highlighting with user-relative literal column expectations. |
| `msbuild-tools-bad-argument-message` | none | — | **P4 / errors.** Passing wrong parameter names to `evaluate_msbuild_property` / `evaluate_msbuild_items` surfaces a generic MCP invocation error instead of listing required parameters such as `project` (2026-04-08 FirewallAnalyzer audit). |
| `compile-check-vs-analyzers-doc` | none | — | **P4 / docs.** `compile_check` can report zero CS diagnostics while `project_diagnostics` lists many CA/IDE warnings on the same workspace — correct by design but confusing (2026-04-08 ITChatBot audit). Clarify CS-only vs analyzer-inclusive semantics in both tool descriptions. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
