# Deep-review rollup

## Scope
- **Generated:** 20260413T164347Z
- **Purpose:** Deep-review rollup
- **Input audit count:** 4
- **Output path:** ai_docs/reports/20260413T164347Z_deep-review-rollup.md

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
| firewallanalyzer |  |  |  |  | ai_docs/audit-reports/20260413T154959Z_firewallanalyzer_mcp-server-audit.md |
| itchatbot |  |  |  |  | ai_docs/audit-reports/20260413T160052Z_itchatbot_mcp-server-audit.md |
| networkdocumentation | 2026-04-09 (UTC run ~16:43–16:52) | Cursor agent loop with `user-roslyn` MCP (stdio host). MCP **prompts** (`prompts/get`) are **not** invokable via this client surface — Phase 16 marked **blocked — client** for per-prompt execution; prompt *descriptors* were not re-run in-session after `workspace_close`. | `d886493` (branch `agent/deep-audit-20260409` in worktree) | `roslyn-mcp` **1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc** | ai_docs/audit-reports/20260409T165300Z_networkdocumentation_mcp-server-audit.md |
| roslyn-backed-mcp | 2026-04-13 (UTC) | Cursor agent session — MCP tools via configured `user-roslyn` server; **prompts not invokable** through this agent path | branch `audit/mcp-full-surface-20260413` (disposable isolation per Phase 0 draft) | `roslyn-mcp` **1.11.2+d0f2680698687bd6fb93c4af5ddabe22f59eb0b2** (from `server_info`) | ai_docs/audit-reports/20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md |

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