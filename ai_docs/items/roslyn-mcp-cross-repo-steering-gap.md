# roslyn-mcp-cross-repo-steering-gap — push cross-repo adoption, or accept current usage? (parked)

**row:** `roslyn-mcp-cross-repo-steering-gap` · **pri:** `Defer` · **size:** `—` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Acceptance

- [ ] Product decision recorded: (a) push adoption via a "prefer Roslyn for C# rename/find-usages/refactor over Edit/Grep/dotnet" line in the consumer repos' OWN `AGENTS.md` (out-of-repo; their backlogs) and/or lower the first-hop activation cost (workspace_load latency is the tax that makes Edit/Grep win for quick edits — auto-load-on-first-semantic-call / faster warm path), or (b) accept current usage as expected

## Evidence

- `ai_docs/reports/20260608T203050Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` (see its Correction block) §3#cross-repo + §5. Source: 2026-06-08 roslyn-mcp multisession retro (corrected). **Weaker evidence — frequency inference; not per-task verified.**

## Context

**CORRECTED 2026-06-08 (Directive #5/#7): the original "zero cross-repo use" premise was a MEASUREMENT ERROR** — the retro scan matched only the bare `mcp__roslyn__` prefix and missed the marketplace-plugin namespace `mcp__plugin_roslyn-mcp_roslyn__` used in consumer repos. Authoritative (JSON `tool_use`) in-window counts: Roslyn-Backed-MCP 89 calls/8 sessions (dev build); **BioRemote 19/3 — incl. a clean 7× `rename_preview`→`rename_apply` cascade on `SecretServerRestClient.DSS.*` NSwag DTOs (worktree `secretserver-nswag-framework-type-dto-audit`) + 2× `evaluate_csharp`**; DotNet-Firewall-Analyzer 1 (`server_info`). TradeWise/BioFileTransfer/DotNet-Network-Documentation: 0 real calls in-window (TradeWise's window was frontend-heavy — playwright+postgres).

So Roslyn IS used cross-repo and works for its flagship semantic feature when invoked; availability is not the issue (global plugin). The real gap is **frequency/reach** (~3 of the BioRemote sessions that touched `.cs` reached for it) → steering/discoverability, not missing capability.

Overlaps: `initiative-executor-roslyn-tool-discovery-experiment`, `tool-surface-pagination-or-tool-sets` (post-`recommend_workflow` steering evidence). Blocked-on: product decision.
