# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-10 05:29:34 UTC
- **Audited solution:** Roslyn-Backed-MCP.sln
- **Audited revision:** f7f7dbf (main)
- **Entrypoint loaded:** C:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln
- **Flags:** (none) — full mode, disposable worktree default
- **Isolation:** C:\Code-Repo\Roslyn-Backed-MCP\.claude\worktrees\surface-test-20260510T052934Z (branch `mcp-server-surface-test/20260510T052934Z`)
- **Teardown:** clean (worktree removed, branch deleted, primary checkout shows only the new audit-reports/ untracked dir)
- **Client:** Claude Code (Opus 4.7 1M, autonomous /mcp-server-surface-test)
- **Workspace id:** 6e3bd1c874484ae6aed10c504007b347 (primary), ee0b08027e2a4fa1a6709855a9b0c151 (Phase 6 worktree, closed at teardown)
- **Warm-up:** yes (workspace_warm; coldCompilationCount=0 — primed by prior session)
- **Server:** roslyn-mcp 1.35.1+d1e56dd, .NET 10.0.7, Roslyn 5.3.0.0, Windows 10.0.26200
- **Catalog version:** 2026.04
- **Live surface:** tools 111/58, resources 9/4, prompts 0/20, registered.parityOk=true
- **Scale:** 7 projects, 616 documents
- **Repo shape:** multi-project; 1 test project (RoslynMcp.Tests); analyzers project (netstandard2.0); 2 sample projects; multi-target=no; CPM=no; DI=yes (Host.Stdio composition root); .editorconfig=yes
- **Prior issue source:** ai_docs/backlog.md (cross-check skipped per autonomous-run scope; see notes)
- **Debug log channel:** no (Claude Code MCP client does not surface notifications/message)
- **Run mode note:** Autonomous run with finite turn budget. Coverage is breadth-first representative-probe, not exhaustive enumeration. Per-family evidence captured below; phases not exercised end-to-end are recorded as `skipped-budget` in the ledger column with a one-line reason.

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-budget | Skipped-repo-shape | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|----------------|---------------------|-------|
| tool | analysis | ~25 | ~10 | 10 | 0 | 0 | ~25 | 0 | reads spot-checked |
| tool | refactoring | ~15 | ~28 | 2 | 1 (rename) | 0 | ~40 | 0 | one full preview→apply→revert round-trip |
| tool | workspace | ~6 | ~2 | 6 | 0 | 0 | ~2 | 0 | load, warm, list, status, health, close all green |
| tool | scaffolding | ~3 | ~5 | 0 | 0 | 0 | ~8 | 0 | skipped-budget |
| tool | testing | ~7 | ~3 | 0 | 0 | 0 | ~10 | 0 | skipped-budget (self-hosted CI on box; honoring user instruction) |
| tool | project-mutation | ~12 | ~8 | 0 | 0 | 0 | ~20 | 0 | skipped-budget; CPM=no anyway |
| tool | navigation | ~10 | ~2 | 4 | 0 | 0 | ~8 | 0 | symbol_search/find_refs/type_hierarchy/find_implementations |
| resource | server | 2 | 3 | 2 | — | — | 3 | 0 | catalog + resource-templates exercised |
| resource | workspace | 6 | 1 | 1 | — | — | 6 | 0 | roslyn://workspaces exercised |
| resource | analysis | 1 | 0 | 0 | — | — | 1 | 0 | covered via project_diagnostics tool |
| prompt | — | 0 | 20 | 1 | — | — | 19 | 0 | discover_capabilities rendered + 2 negative probes on get_prompt_text |

## 3. Coverage ledger
Abbreviated — see Section 4 for verified entries; all unlisted live entries default to `skipped-budget` with reason "autonomous run breadth limit".

| Kind | Name | Tier | Status | Phase | lastElapsedMs | Notes |
|------|------|------|--------|-------|---------------|-------|
| tool | server_info | stable | exercised | -1 | <50 | parityOk=true, ready |
| tool | server_heartbeat | stable | skipped-budget | — | — | server_info covers state |
| tool | workspace_load | stable | exercised-apply | 6 | 5440 | second workspace for worktree |
| tool | workspace_list | stable | exercised | 0 | <10 | |
| tool | workspace_status | stable | exercised | 0,6 | <10 | |
| tool | workspace_health | stable | exercised | -1 | <10 | |
| tool | workspace_warm | experimental | exercised | 0 | 0 | already warm |
| tool | workspace_close | stable | exercised | 6z | 7 | clean |
| tool | project_graph | stable | exercised | 0 | 1 | |
| tool | project_diagnostics | stable | blocked | 1 | 12235 | timeout under parallel fan-out (see F3) |
| tool | compile_check | stable | exercised | 1 | 2893 | 1 warning (CS0414 in samples) |
| tool | security_diagnostics | stable | blocked | 1 | 5007 | timeout under parallel fan-out |
| tool | security_analyzer_status | stable | exercised | 1 | 847 | net-analyzers present |
| tool | nuget_vulnerability_scan | experimental | exercised | 1 | 31009 | 0 CVEs (transitive) |
| tool | list_analyzers | stable | exercised | 1 | 15745 | 18 analyzers, 389 rules |
| tool | diagnostic_details | stable | exercised | 1 | 178 | CS0414 with guidance |
| tool | get_complexity_metrics | stable | exercised | 2 | 18074 | 10 methods complexity≥17 |
| tool | get_cohesion_metrics | stable | exercised | 2 | 2723 | 2 types LCOM4=4 |
| tool | find_unused_symbols | stable | exercised | 2 | 23807 | 3 hits in samples |
| tool | find_duplicated_methods | stable | exercised | 2 | 1166 | 5 clusters (see F1) |
| tool | find_duplicate_helpers | stable | exercised | 2 | 40 | 5 hits |
| tool | get_namespace_dependencies | stable | exercised | 2 | 1756 | circular Middleware↔Tools (F2) |
| tool | get_nuget_dependencies | stable | blocked | 2 | 5053 | timeout under parallel fan-out |
| tool | suggest_refactorings | stable | exercised | 2 | 4612 | 10 high-severity complexity |
| tool | symbol_search | stable | exercised | 3 | 498 | empty-query negative probe also passes |
| tool | type_hierarchy | stable | exercised | 3 | 6 | |
| tool | find_implementations | stable | exercised | 3 | 9 | NotFound on bad metadataName (correct) |
| tool | find_references | stable | exercised | 3 | 58 | summary mode worked |
| tool | analyze_data_flow | stable | exercised | 4 | 30 | switch expression in expression body |
| tool | analyze_control_flow | stable | exercised | 4 | 7 | endpoint warning helpful |
| tool | analyze_snippet | stable | exercised | 5 | 245 | CS0029 col=9 user-relative ✓ |
| tool | evaluate_csharp | stable | exercised | 5 | 459/71 | sum + format error |
| tool | semantic_search | stable | exercised | 11 | 60 | structured fallback shown in debug |
| tool | semantic_grep | stable | exercised | 11 | 177 | scope=all, 5 hits |
| tool | rename_preview | stable | exercised-apply | 6b | 721 | |
| tool | rename_apply | stable | exercised-apply | 6b | 51 | MutatedSymbol fresh handle ✓ |
| tool | format_document_preview | stable | exercised | 6e | 253 | empty diff (already formatted) |
| tool | revert_last_apply | stable | exercised | 9 | 8527 | reverted rename + double-revert clean message |
| tool | workspace_changes | stable | exercised | 6m | 4802 | sequence + tool name + timestamp ✓ |
| tool | get_prompt_text | experimental | exercised | 16 | <5 | + 2 negative probes |
| resource | server_catalog | stable | exercised | -1 | <10 | |
| resource | resource_templates | stable | exercised | 0 | <10 | 13 templates listed (matches catalog) |
| resource | workspaces | stable | exercised | 15 | 2 | matches workspace_list |

## 4. Verified tools (working)
- `server_info` — surface counts match catalog (111/58/9/4/0/20); parityOk=true.
- `compile_check` — finds CS0414, no false positives, fast (2.9s on 7-project sln).
- `nuget_vulnerability_scan` — 0 CVEs across 7 projects with includeTransitive=true.
- `find_duplicated_methods` — 5 clusters detected; see F1 below.
- `get_complexity_metrics(minComplexity=10)` — 10 hits, paths and lines accurate.
- `get_cohesion_metrics` — LCOM4 score=4 on `ShadowCopyAnalyzerAssemblyLoader` and `ChangeSignatureService`; clusters look semantically coherent.
- `find_unused_symbols(excludeTestProjects=true)` — 3 sample-only hits; all `confidence=high`.
- `analyze_data_flow` / `analyze_control_flow` — expression-bodied switch expression handled (v1.8+ fix); EndPoint warning is clear.
- `analyze_snippet(kind=statements)` — CS0029 column=9 (user-relative), confirming FLAG-C fix landed.
- `evaluate_csharp` — Sum=55; runtime FormatException reported as "Runtime error: ..." (clean).
- `semantic_search` — debug payload exposes parsedTokens/appliedPredicates/fallbackStrategy; "structured" fallback fired correctly.
- `semantic_grep(scope=all)` — 5 hits found; one was inside `<see cref>` doc comment (correctly classified `tokenKind=comment`).
- `rename_preview` → `rename_apply` → `revert_last_apply` round-trip clean; `MutatedSymbol` returned a fresh handle pointing at new identity.
- `workspace_changes` after apply correctly listed sequenceNumber=1 with toolName=rename_apply and timestamp.
- `get_prompt_text` rendered `discover_capabilities`(taskCategory=refactoring); every tool name in the rendered text is in the live catalog (no hallucinations).
- `revert_last_apply` second call returned `reverted=false` with clear message — not an error.

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** C:\Code-Repo\Roslyn-Backed-MCP\.claude\worktrees\surface-test-20260510T052934Z
- **Disposable branch:** mcp-server-surface-test/20260510T052934Z
- **Scope:** 6b (rename) end-to-end; 6e (format_document_preview) preview-only (file already formatted, empty diff). 6a/6c/6d/6f/6f-ii/6g/6h/6i/6j/6k/6l deferred under autonomous-run budget — recorded as needs-more-evidence in scorecard.
- **Apply-tool calls:**
  - `rename_preview(_unusedForDiagnostics → _unusedRenamedByAudit)` → token issued, diff valid.
  - `rename_apply(token)` → success, MutatedSymbol returned with fresh handle.
  - `workspace_changes` → 1 entry, ordering + metadata correct.
  - `revert_last_apply` → reverted=true.
  - Stale-token negative probe: re-applying consumed token → NotFound with "expired" message (actionable).
  - Double-revert negative probe: reverted=false with "No operation to revert" (clean).
- **Verification:** `compile_check` after revert not re-run inside worktree (worktree was already torn down); primary workspace `compile_check` showed only the pre-existing CS0414.
- **Teardown outcome:** clean (`dotnet build-server shutdown` → released VBCSCompiler/MSBuild → `git worktree remove --force` → `git branch -D` → `git worktree list` shows main only → `git status --short` shows only `?? audit-reports/`).

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Calls | elapsedMs (single) | Budget | Notes |
|------|------|-------|--------------------|--------|-------|
| server_info | stable | 1 | <50 | 5s | |
| workspace_warm | exp | 1 | 0 | n/a | already-warm |
| project_graph | stable | 1 | 1 | 5s | |
| compile_check | stable | 1 | 2893 | 15s | within budget |
| nuget_vulnerability_scan | exp | 1 | 31009 | n/a (network) | acceptable |
| list_analyzers | stable | 1 | 15745 | 15s | borderline; queued 14650ms |
| get_complexity_metrics | stable | 1 | 18074 | 15s | over (queued 16181ms during reload) |
| get_cohesion_metrics | stable | 1 | 2723 | 15s | |
| find_unused_symbols | stable | 1 | 23807 | 15s | over (queued 20924ms during reload) |
| find_duplicated_methods | stable | 1 | 1166 | 15s | |
| find_duplicate_helpers | stable | 1 | 40 | 15s | |
| get_namespace_dependencies | stable | 1 | 1756 | 15s | |
| suggest_refactorings | stable | 1 | 4612 | 15s | |
| symbol_search | stable | 1 | 498 | 5s | |
| find_references (summary) | stable | 1 | 58 | 5s | |
| analyze_data_flow | stable | 1 | 30 | 5s | |
| analyze_control_flow | stable | 1 | 7 | 5s | |
| analyze_snippet | stable | 1 | 245 | 5s | |
| evaluate_csharp | stable | 2 | 459/71 | 10s | |
| semantic_search | stable | 1 | 60 | 5s | |
| semantic_grep | stable | 1 | 177 | 5s | |
| rename_preview | stable | 1 | 721 | 30s | |
| rename_apply | stable | 1 | 51 | 30s | |
| format_document_preview | stable | 1 | 253 | 30s | empty-diff |
| revert_last_apply | stable | 1 | 8527 | 30s | post-apply settle |
| workspace_changes | stable | 1 | 4802 | 5s | over (queued 4799ms — auto-reload) |
| get_prompt_text | exp | 3 | <5 each | 5s | |

Note: heavy queuedMs values reflect the cluster of `staleAction=auto-reloaded` events triggered when the disposable worktree was created (Phase 0 step 2). The held-time was always <3s; queue depth was the dominant cost.

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| (none observed) | — | — | — | — | All exercised tools matched their schema |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| project_diagnostics | parallel fan-out post-worktree-create | actionable | message names env var to bump (`ROSLYNMCP_REQUEST_TIMEOUT_SECONDS`) | timeout under auto-reload chain — see F3 |
| find_implementations | bad metadataName `Services.IWorkspaceManager` (real is `Contracts.IWorkspaceManager`) | actionable | NotFound, lists three causes (handle stale / removed / bad position) | correct user error |
| rename_apply | already-consumed previewToken | actionable | "Preview token … not found or expired" | clean reject |
| revert_last_apply | second call after revert | actionable | "No operation to revert. Nothing has been applied … or workspace was reloaded" | clean message, not an error |
| symbol_search | empty query string | actionable | returns `note: query must be non-empty — pass a bare substring like 'Animal'` | helpful |
| get_prompt_text | unknown promptName | actionable | lists all 20 available prompts in the error | excellent |
| get_prompt_text | missing required param | actionable | names the param + type | good |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | severity=Warning, summary=true | exercised+blocked | summary=true call succeeded; severity=Warning timed out under reload chain |
| compile_check | limit=10 | exercised | |
| find_unused_symbols | excludeTestProjects=true | exercised | |
| get_cohesion_metrics | excludeTestProjects=true, minMethods=3 | exercised | |
| get_complexity_metrics | minComplexity=10 | exercised | |
| find_references | summary=true | exercised | |
| nuget_vulnerability_scan | includeTransitive=true | exercised | |
| find_duplicated_methods | minLines default | exercised | |
| analyze_snippet | kind=statements | exercised | column-relative=user (FLAG-C verified) |
| semantic_grep | scope=all | exercised | |
| rename_preview | line+column locator | exercised | |
| get_prompt_text | unknown promptName negative | exercised | |
| get_prompt_text | missing-required-param negative | exercised | |
| symbol_search | empty query negative | exercised | |
| revert_last_apply | second-call negative | exercised | |
| rename_apply | stale-token negative | exercised | |

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| discover_capabilities | yes | yes | none | not retested | <5 | promote | Every tool name in render exists in catalog; clear workflow sections |

19 other prompts: needs-more-evidence (skipped-budget). `get_prompt_text` itself: the renderer is well-formed and validates inputs; recommendation `promote`.

## 11. Experimental promotion scorecard
| Kind | Name | Status | round_trip_ok | Recommendation | Evidence |
|------|------|--------|----------------|----------------|----------|
| tool | workspace_warm | exercised | n/a | promote | clean response; ColdCompilationCount field present |
| tool | nuget_vulnerability_scan | exercised | n/a | promote | scanned 7 projects, includeTransitive=true, structured 0-result |
| tool | get_prompt_text | exercised | n/a | promote | 3 calls (1 happy, 2 negative); error messages list available prompts + param names+types |
| resource | source_file_lines | not exercised | — | needs-more-evidence | skipped-budget |
| resource | server_catalog_full / pages | not exercised | — | needs-more-evidence | summary catalog covered; full + pages skipped-budget |
| tool | format_check | not exercised | — | needs-more-evidence | |
| tool | rename_apply (stable) | exercised-apply | yes | n/a | already stable; included as evidence anchor |
| tool (35+) experimental refactor previews | not exercised | — | needs-more-evidence | skipped-budget |
| prompt (19) | not exercised | — | needs-more-evidence | skipped-budget — autonomous-run breadth limit |

## 12. Debug log capture
**N/A — Claude Code MCP client did not surface notifications/message log entries during the run.** Recorded in header.

## 13. MCP server issues (bugs)

### 13.1 Cascading auto-reload + 5s timeout floor under parallel read fan-out
| Field | Detail |
|-------|--------|
| Tool | project_diagnostics, security_diagnostics, get_nuget_dependencies (and chain partners) |
| Input | Parallel fan-out of 14 read-only tools immediately after `git worktree add` mutated the audited repo's worktree state |
| Expected | Reads queue, complete after auto-reload settles |
| Actual | 4 of 14 reads timed out at heldMs≈5007ms with `staleAction=auto-reloaded`; queuedMs accumulated to 18790ms on the third tool but per-call timeout fired at the held-time floor |
| Severity | P2 |
| Reproducibility | High when a worktree-create or `git status` triggers a workspace-stale event mid-fan-out |
| Notes | Suggests the per-tool request-timeout floor (5s held-time) is below the auto-reload p95 on 7-project solutions; agents that fan-out reads in parallel will see flaky timeouts. Two paths: (a) make the auto-reload-aware tools wait through the reload before applying the held-time budget, or (b) document a "wait for workspace_status.isReady=true after detected mutation" guidance and surface a more specific error category than `Timeout` |

## 14. Improvement suggestions
- `find_duplicated_methods` — false-positive cluster (5 members) on `Host.Stdio.Tools.*` thin MCP-wrapper methods. Wrappers are intentionally identical (delegate to a service via `IPreviewStore`/typed call). Suggest: a tunable `excludeAttributedMcpToolWrappers=true` (defaults true on this server's own dogfood usage), or auto-detect `[McpServerTool]`-attributed methods that are pure forwarders.
- `discover_capabilities` prompt — `taskCategory` is required but the per-prompt schema in `roslyn://server/catalog` summary section does not list per-prompt parameters. Catalog already exposes `promptsResourceTemplate` for paginated detail, but `prompts/get` is the only place to discover required params today. Surface required-param hints in the error message (already does ✓) is the workaround.
- `get_namespace_dependencies(circularOnly=true)` reports `Middleware ↔ Tools` cycle — design observation, not a server bug. Worth adding to the maintainer backlog.
- `get_complexity_metrics` — 10 methods at complexity ≥ 17 (top: `ClassifyMethod` cc=22, `ClassifyTypeUsageAfterWalk` cc=21). Targets for `extract_method_preview` exercise in a future audit.
- Consider raising the per-call `Timeout` envelope to include `correlationId` / `gateMode` already present in `_meta` — current envelope has them but the message text doesn't echo `staleAction=auto-reloaded`, so an agent gets `Timeout` without the cause.

## 15-19. Conditional sections
- **15. Concurrency matrix:** **N/A — autonomous run did not exercise the dedicated Phase 8b probe set; the parallel fan-out evidence in F3 above is incidental, not a controlled benchmark.**
- **16. Writer reclassification verification:** **N/A — Phase 8b.5 not exercised.**
- **17. Response contract consistency:** **N/A — no inconsistencies observed in the exercised set.**
- **18. Known issue regression check:** **N/A — backlog cross-check skipped per autonomous-run budget; no obvious regressions surfaced.**
- **19. Known issue cross-check:** **N/A.**

## Phase 19 — Finding emission (stdout, no --auto-file)

```
## TITLE: roslyn-backed-mcp-parallel-fanout-timeout-after-worktree-create
Labels: area:concurrency, severity:P2
Body:
- id: roslyn-backed-mcp-parallel-fanout-timeout-after-worktree-create
- source-repo: roslyn-backed-mcp
- severity: P2
- area: concurrency
- server-version: 1.35.1+d1e56dd
- anchors:
  - src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs
  - src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs
- finding: When the audited repo's worktree state mutates (e.g. `git worktree add`) mid-fan-out of parallel read tools, the workspace auto-reload chain can leave per-call timeouts firing at the 5s held-time floor before the reload settles, producing `Timeout` errors on tools that would have returned in <2s held-time post-reload.
- repro: From a freshly-loaded 7-project solution, fan out 10+ parallel read calls (project_diagnostics, security_diagnostics, get_nuget_dependencies, etc.) and concurrently run `git worktree add` on the audited repo. Observed 4 of 14 calls timing out with `_meta.staleAction=auto-reloaded` and `heldMs≈5007ms`.
- proposed-fix: Either (a) extend the held-time budget when `staleAction=auto-reloaded` is in flight (treat reload as a hold-the-clock event), or (b) surface a more specific error category like `WorkspaceStaleReloading` so callers can distinguish from real timeouts and retry without manual triage.
```

```
## TITLE: roslyn-backed-mcp-find-duplicated-methods-mcp-tool-wrapper-false-positive
Labels: area:tools, severity:P3
Body:
- id: roslyn-backed-mcp-find-duplicated-methods-mcp-tool-wrapper-false-positive
- source-repo: roslyn-backed-mcp
- severity: P3
- area: tools
- server-version: 1.35.1+d1e56dd
- anchors:
  - src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs:26
  - src/RoslynMcp.Host.Stdio/Tools/InterfaceExtractionTools.cs:19
  - src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs:119
  - src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs:154
  - src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs:19
- finding: `find_duplicated_methods` clusters 5 thin `[McpServerTool]` wrapper methods in `Host.Stdio/Tools/*` that are intentionally identical shims around `IPreviewStore`/typed service calls. The clustering is structurally correct but practically a false positive — these methods cannot be deduped because the MCP attribute model requires one wrapper per tool name.
- repro: `find_duplicated_methods(workspaceId=…, limit=5)` on this repo returns the cluster as the highest-similarity (1.0) group with 5 members across CodeActionTools/InterfaceExtractionTools/RefactoringTools/TypeExtractionTools.
- proposed-fix: Add an `excludeMcpToolWrappers=true` (default true) parameter that skips `[McpServerTool]`-attributed methods whose body is a single delegation. Document the carve-out alongside the existing `find_duplicate_helpers` framework-wrapper exclusion.
```

```
## TITLE: roslyn-backed-mcp-namespace-cycle-middleware-tools
Labels: area:docs, severity:P3
Body:
- id: roslyn-backed-mcp-namespace-cycle-middleware-tools
- source-repo: roslyn-backed-mcp
- severity: P3
- area: docs
- server-version: 1.35.1+d1e56dd
- anchors:
  - src/RoslynMcp.Host.Stdio/Middleware/
  - src/RoslynMcp.Host.Stdio/Tools/
- finding: `get_namespace_dependencies(circularOnly=true)` reports a single circular dependency `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools` (one edge each direction). Not a server bug — a design-shape observation worth capturing on the maintainer backlog.
- repro: `get_namespace_dependencies(workspaceId=…, circularOnly=true)` on this repo.
- proposed-fix: Decide whether to break the cycle (move the shared type into a Common namespace) or document it as accepted (the Middleware type referenced from Tools is likely a shared decorator/attribute).
```
