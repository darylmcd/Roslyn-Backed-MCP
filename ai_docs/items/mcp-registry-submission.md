# mcp-registry-submission — submit server.json to the public MCP Registry + badge flip

**row:** `mcp-registry-submission` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `.claude-plugin/server.json`
- `README.md:4` (badge)
- `README.md:139`

## Acceptance

- [ ] `.claude-plugin/server.json` (name `io.github.darylmcd/roslyn-mcp`, NuGet `Darylmcd.RoslynMcp`, dnx) submitted to the public MCP Registry
- [ ] On approval: README badge flipped from "submission pending" to the live registry URL; registry-aware install snippet added

## Evidence

- Roadmap names registry publication as future direction (`docs/roadmap.md` § Claude Code Plugin Distribution); README badge still "submission pending"; no prior backlog row. Source: 2026-05-28 discovery-sweep roadmap-reconcile.

## Context

Repo-side prep is already shipped (server.json draft via #502 + the registry-readiness check); only the external submit + badge-flip remain. Blocked-on: external — MCP Registry approval (maintainer submission action, not a code change).
