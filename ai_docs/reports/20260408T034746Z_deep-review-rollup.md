# Deep-review rollup

## Scope
- **Generated:** 20260408T034746Z
- **Purpose:** Deep-review rollup
- **Input audit count:** 4
- **Output path:** ai_docs\reports\20260408T034746Z_deep-review-rollup.md

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
| dotnet-firewall-analyzer_rw-lock |  |  |  |  | ai_docs\audit-reports\20260407T223900Z_dotnet-firewall-analyzer_rw-lock_mcp-server-audit.md |
| itchatbot_rw-lock | 2026-04-08 (UTC tool timestamps ~03:39–03:40Z) | Cursor (MCP server id: user-roslyn); MCP **prompts** not invoked via prompts API | 8c2e3f6 (short) | roslyn-mcp 1.6.1+f16452924ef03cd3ed17dc36b53efab29c72217a; catalog 2026.03; tools 56 stable / 67 experimental; resources 7; prompts 16 experimental | ai_docs\audit-reports\20260408T033920Z_itchatbot_rw-lock_mcp-server-audit.md |
| networkdocumentation_rw-lock | 2026-04-08 (UTC run; filename timestamp `20260408T034457Z`) | Cursor agent with `user-roslyn` MCP (`roslyn-mcp` host **1.6.1+f16452924ef03cd3ed17dc36b53efab29c72217a**) | `main` @ `8d103fe` (short); working tree had uncommitted `ai_docs` edits at run time | `server_info`: catalog **2026.03**; tools **56 stable / 67 experimental**; resources **7 stable**; prompts **16 experimental**; `runtime` **.NET 10.0.5**; `roslynVersion` **5.3.0.0** | ai_docs\audit-reports\20260408T034457Z_networkdocumentation_rw-lock_mcp-server-audit.md |
| roslynmcp | 2026-04-08T03:46:00Z (report finalized UTC; tool calls ~03:41–03:45Z) | Cursor agent session with `user-roslyn` MCP; prompts not invoked via MCP prompt API | (not pinned in session — workspace from local disk) | `roslyn-mcp` **1.6.1** (commit f16452924ef03cd3ed17dc36b53efab29c72217a per `server_info`) | ai_docs\audit-reports\20260408T034600Z_roslynmcp_mcp-server-audit.md |

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