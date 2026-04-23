# Deep-review rollup

<!-- purpose: Synthesized deep-review rollup for the 2026-04-22 audit batch. -->

## Scope
- **Generated:** 20260422T143323Z
- **Purpose:** Deep-review rollup
- **Input audit count:** 5
- **Output path:** ai_docs/reports/20260422T143323Z_deep-review-rollup.md

## Input audits
Raw inputs that lived under `ai_docs/audit-reports/2026-04-*` were **retired to `ai_docs/backlog.md` on 2026-04-22** (see rows `mcp-audit-rollup-2026-04-13-22`, `validate-workspace-overallstatus-false-positive`, `workspace-close-missing-solution-on-disk`); the table below is historical.

| Repo | Date | Client | Revision | Server | File (historical) |
|------|------|--------|----------|--------|------|
| firewallanalyzer | 2026-04-15 | Claude Code (Opus 4.6 / 1M context) with strict `PreToolUse` hook on `*_apply` | `9123671` on branch `audit/experimental-promotion-20260415` (disposable worktree of `main`) | `roslyn-mcp v1.18.0+172d3c5118178eae2664768ced630b38d9418309` (.NET 10.0.6, Roslyn 5.3.0.0) | `20260415T140000Z_firewallanalyzer_experimental-promotion.md` (removed) |
| firewallanalyzer |  |  |  |  | `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` (removed) |
| itchatbot | 2026-04-15T14:04:03Z | Claude Code with native Roslyn MCP binding (`mcp__roslyn__*` tools) | worktree branch `worktree-experimental-promotion-2026-04-15` from `main` (commit `ee167149723cc2392c43715312f93371a631e063`) | `roslyn-mcp 1.18.0+172d3c5118178eae2664768ced630b38d9418309` | `20260415T140403Z_itchatbot_experimental-promotion.md` (removed) |
| itchatbot |  |  |  |  | `20260413T160052Z_itchatbot_mcp-server-audit.md` (removed) |
| networkdocumentation | 2026-04-15 (UTC 14:03:02Z start — 15:25:00Z finish) | Claude (Anthropic) via Roslyn MCP stdio | commit `8395fd8` on throwaway branch `exp-promotion-2026-04-15` | roslyn-mcp 1.18.0 (runtime .NET 10.0.6, Roslyn 5.3.0.0, Windows 10.0.26200) | `20260415T140302Z_networkdocumentation_experimental-promotion.md` (removed) |

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
