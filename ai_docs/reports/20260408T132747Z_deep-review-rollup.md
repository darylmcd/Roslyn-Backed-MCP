# Deep-review rollup

## Scope
- **Generated:** 20260408T132747Z
- **Purpose:** 2026-04-08 multi-repo deep-review batch
- **Input audit count:** 5
- **Output path:** ai_docs/reports/20260408T132747Z_deep-review-rollup.md

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
| roslyn-backed-mcp | 2026-04-08 (UTC ~13:06–13:12Z) | Cursor + `user-roslyn` MCP | `7c9fe00` (`main`) | roslyn-mcp 1.7.0+46e9f728, catalog 2026.03 | ai_docs/audit-reports/20260408T130615Z_roslyn-backed-mcp_mcp-server-audit.md |
| roslyn-backed-mcp | 2026-04-08 (UTC ~13:13Z) | Cursor + `user-roslyn` MCP | `7c9fe00` (`main`) | roslyn-mcp 1.7.0+46e9f728 | ai_docs/audit-reports/20260408T131317Z_roslyn-backed-mcp_mcp-server-audit.md |
| itchatbot | 2026-04-08 (`20260408T132041Z`) | Cursor + `user-roslyn` MCP | `8c2e3f6` | roslyn-mcp 1.7.0+46e9f728 | ai_docs/audit-reports/20260408T132041Z_itchatbot_mcp-server-audit.md |
| firewallanalyzer | 2026-04-08 (`20260408T132500Z`) | Cursor + `user-roslyn` MCP | working tree | roslyn-mcp 1.7.0+46e9f728 | ai_docs/audit-reports/20260408T132500Z_firewallanalyzer_mcp-server-audit.md |
| networkdocumentation | 2026-04-08 (`20260408T132800Z`) | Cursor + `user-roslyn` MCP | `8d103fe` (branch `audit/roslyn-mcp-deep-review-20260408`) | roslyn-mcp 1.7.0+46e9f728 | ai_docs/audit-reports/20260408T132800Z_networkdocumentation_mcp-server-audit.md |

## Repo matrix coverage
| Bucket | Covered | Evidence | Notes |
|--------|---------|----------|-------|
| Small or single-project repo | partial | roslyn-backed-mcp (6 projects) | Host repo |
| Multi-project repo with tests | yes | itchatbot (34), firewallanalyzer (11), networkdocumentation (8) | Varied shapes |
| DI-heavy repo | yes | itchatbot, firewallanalyzer | |
| Source-generator repo | yes | itchatbot, firewallanalyzer | |
| Central Package Management or multi-targeting repo | partial | firewallanalyzer CPM; roslyn-backed-mcp CPM | No multi-target exercise |
| Large solution representative repo | yes | itchatbot 34 projects | |

## Client coverage
| Client | Full-surface | Notes |
|--------|--------------|-------|
| Cursor (`user-roslyn`) | partial | All five runs: MCP prompts blocked; abbreviated phases common; Phase 8b concurrency matrix not completed on any run |

## Deduped issues
| Key | Severity | Evidence | Backlog action |
|-----|----------|----------|----------------|
| `project_diagnostics` severity filter clears aggregate totals while unfiltered has warnings | P2 | 130615, 131317 | `project-diagnostics-filter-totals` (existing) |
| `test_run` Windows file locks / copy errors | P2 | 130615 | `test-run-failure-envelope` (new) |
| `test_run` generic MCP invocation error (no structured body) | P2 | 131317 | `test-run-failure-envelope` (new) |
| `workspace_list` count>1 for duplicate `workspace_load` of same `.sln` | P3 | 130615 (131317 did not reproduce) | `workspace-session-deduplication` (new) |
| `revert_last_apply` success but on-disk file still had reverted text until reload | P3 | 132800 | `revert-last-apply-disk-consistency` (new) |
| `semantic_search` returns count 0 on long NL query; shorter query returned hits | P3 | 132500 | `semantic-search-zero-results-verbose-query` (new) |
| `semantic_search` Task<bool> query relevance / ranking | P3 | 132041 | `semantic-search-async-modifier-doc`, `get-completions-ranking` (existing; partial) |
| `compile_check` 0 CS vs `project_diagnostics` 143 analyzer warnings — role confusion | P4 | 132041 | `compile-check-vs-analyzers-doc` (new) |
| `analyze_snippet` CS0029 span UX (literal vs declaration region) | P4 | 132500 | `analyze-snippet-cs0029-literal-span` (new) |
| `evaluate_msbuild_property` / `evaluate_msbuild_items` wrong parameter names → generic MCP error | P4 | 132500 | `msbuild-tools-bad-argument-message` (new) |
| `find_references` malformed handle returns structured error (not silent empty) | — | 130615 | `error-response-observability` — regression check: partially improved vs silent empty |
| `impact_analysis` bad handle → actionable NotFound | — | 132800 | Expected; no new row |

## Blocked-by-client summary
- **Prompts (16):** no run invoked MCP prompt RPC via `call_mcp_tool` — catalog rows correctly marked `blocked` with client limitation.
- **MCP `notifications/message`:** not surfaced in Cursor channel on these runs — debug log capture sections empty by design.

## Candidate closures
| Source id | Evidence | Notes |
|-----------|----------|-------|
| Duplicate `workspace_list` | 131317: `count: 1` after single load | Contradicts 130615 (`count: 2`); treat as intermittent/host-dependent — do not close `workspace-session-deduplication` until understood |

## Backlog actions
- Opened **P2:** `test-run-failure-envelope`.
- Opened **P3:** `workspace-session-deduplication`, `revert-last-apply-disk-consistency`, `semantic-search-zero-results-verbose-query`.
- Opened **P4:** `compile-check-vs-analyzers-doc`, `analyze-snippet-cs0029-literal-span`, `msbuild-tools-bad-argument-message`.
- Confirmed existing rows still apply: `project-diagnostics-filter-totals`, `error-response-observability`, `semantic-search-async-modifier-doc`, `get-completions-ranking`.
