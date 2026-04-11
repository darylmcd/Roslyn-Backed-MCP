# Deep-review rollup

## Scope
- **Generated:** 20260409T170236Z
- **Purpose:** Deep-review rollup
- **Input audit count:** 5
- **Output path:** ai_docs\reports\20260409T170235Z_deep-review-rollup.md

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
| firewall-analyzer |  |  |  |  | ai_docs\audit-reports\20260408T195030Z_firewall-analyzer_mcp-server-audit.md |
| firewallanalyzer | 2026-04-09 (UTC run completion ~16:52Z) | Cursor agent session with `user-roslyn` MCP (stdio). **MCP `prompts/*`**: not exposed as invocable tools in this client path → Phase 16 **blocked (client)**. **MCP log notifications**: not surfaced in tool results → **no** in header. | `cbadadd` (worktree branch `wt/mcp-deep-review-20260409`) | roslyn-mcp **1.8.2** (`productShape`: local-first), **Roslyn** 5.3.0.0, **runtime** .NET 10.0.5, **os** Microsoft Windows 10.0.26200 | ai_docs\audit-reports\20260409T165230Z_firewallanalyzer_mcp-server-audit.md |
| itchatbot | 2026-04-09 (UTC run stamp `20260409T164306Z`) | Cursor agent session (`user-roslyn` MCP); MCP prompt invocation (`prompts/get`) not verified in this client | branch `mcp-audit/20260409T164306Z` @ `c728daf` (merge PR #47) | `roslyn-mcp` **1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc** | ai_docs\audit-reports\20260409T164306Z_itchatbot_mcp-server-audit.md |
| networkdocumentation | 2026-04-09 (UTC run ~16:43–16:52) | Cursor agent loop with `user-roslyn` MCP (stdio host). MCP **prompts** (`prompts/get`) are **not** invokable via this client surface — Phase 16 marked **blocked — client** for per-prompt execution; prompt *descriptors* were not re-run in-session after `workspace_close`. | `d886493` (branch `agent/deep-audit-20260409` in worktree) | `roslyn-mcp` **1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc** | ai_docs\audit-reports\20260409T165300Z_networkdocumentation_mcp-server-audit.md |
| roslyn-backed-mcp | 2026-04-09 (run ended ~16:57 UTC) | Cursor agent with `user-roslyn` MCP (stdio host). Some tool invocations (`test_run` full suite, `get_code_actions` once) returned generic transport errors without structured server payloads. | branch `audit/deep-review-20260409T120000Z`, commit `944db4c` (worktree HEAD) | roslyn-mcp **1.8.2** (`1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc` from `server_info`) | ai_docs\audit-reports\20260409T165739Z_roslyn-backed-mcp_mcp-server-audit.md |

## Repo matrix coverage
| Bucket | Covered | Evidence | Notes |
|--------|---------|----------|-------|
| Small or single-project repo | | | |
| Multi-project repo with tests | | | |
| DI-heavy repo | | | |
| Source-generator repo | | | |
| Central Package Management or multi-targeting repo | | | |
| Large solution representative repo | | | |

## Client coverage
| Client | Full-surface | Notes |
|--------|--------------|-------|
| | | |

## Deduped issues
| Key | Severity | Evidence | Backlog action |
|-----|----------|----------|----------------|
| | | | |

## Blocked-by-client summary
- 

## Candidate closures
| Source id | Evidence | Notes |
|-----------|----------|-------|
| | | |

## Backlog actions
- 