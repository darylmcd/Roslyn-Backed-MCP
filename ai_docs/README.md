# AI Docs Index

<!-- purpose: Route agents to canonical AI docs; index only — no embedded policy prose. -->

This directory is the canonical AI-facing documentation tree. Use this file to find what to load for a task.

## Core References (read on every session)

| File | Purpose |
|------|---------|
| `../CI_POLICY.md` | Validation and merge-gating policy |
| `workflow.md` | Git/branch/worktree/PR workflow |
| `runtime.md` | Build, test, run commands; user/session-scoped Roslyn MCP intent versus live-probe evidence |
| `bootstrap-read-tool-primer.md` | Canonical pattern-to-tool cheat sheet for read-side MCP usage |
| `planning_index.md` | Router for in-repo planning docs and scope boundaries |
| `backlog.md` | Open work only; sync when closing rows |
| `architecture.md` | Code Map, entry points, layers, data flow, and key abstractions |

## Domain Entry Points (read when touching that layer)

| File | Covers |
|------|--------|
| `architecture.md` (§ Code Map) | Per-layer entry points: MCP host, tool wiring, DTO contracts, Roslyn services |
| `ai_docs/domains/tool-usage-guide.md` | How to choose the right tools and verify changes |

## Reference Material (read on demand)

| File | Purpose |
|------|---------|
| `ai_docs/references/testing.md` | Test patterns, commands, and coverage guidance |
| `known-flakes.md` | Registry of known/quarantined test flakes (currently holds no active flakes) |
| `ai_docs/references/environment-variables.md` | `ROSLYNMCP_*` environment variable reference |
| `ai_docs/references/tooling/mcp-clients.md` | MCP client integration notes |
| `ai_docs/references/mcp-server-best-practices.md` | MCP error-model, filter pipeline, and protocol hygiene guidance |

## Procedures And Prompts

| File | Purpose |
|------|---------|
| `ai_docs/procedures/deep-review-program.md` | Multi-repo deep-review matrix, raw-vs-rollup outputs, and backlog intake rules |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Reference procedure for merging deep-review findings back into `backlog.md` |
| `ai_docs/procedures/deep-review-command-reference.md` | Shell commands for import, rollup, compare, and batch review workflows |
| `ai_docs/prompts/profile-large-solution.md` | Runbook for collecting 50+ project Roslyn MCP profiling evidence |
| `ai_docs/prompts/stress-test-external-repo.md` | Performance and correctness stress-test protocol for large external solutions |
| `ai_docs/prompts/roslyn-mcp-multisession-retro.md` | Cross-repo retrospective prompt that scans Claude Code and Codex session transcripts for Roslyn MCP issues, missing-tool gaps, and recommendations |
| `ai_docs/prompts/backlog-sweep-addenda.md` | Repo-specific `/backlog-remediate` addenda; historical filename retained for compatibility |

## Reports And Archive

| File | Purpose |
|------|---------|
| `ai_docs/items/` | Per-row backlog detail (`items/<id>.md`); owned by `backlog.md` |
| `ai_docs/plans/` | Plan trees routed via `planning_index.md`; timestamped `*_backlog-remediate/` trees are generated |
| `ai_docs/audits/20260825-1440/` | Logging-audit run output (1 of 3 retention slots) |
| `ai_docs/audit-reports/README.md` | Raw MCP audit outputs and `ai_docs/audit-reports/deep-review-session-checklist.md` |
| `ai_docs/reports/README.md` | Synthesized rollups and cross-cutting audit reports |
| `ai_docs/archive/README.md` | Archive policy |

---

## Task-Scoped Reading Guide

| Task | Files to read |
|------|---------------|
| First session / orientation | `AGENTS.md` -> `../CI_POLICY.md` -> `workflow.md` -> `runtime.md` -> `architecture.md` |
| Planning or "what next?" in this repo | `planning_index.md` -> `backlog.md` |
| Fix a bug in Roslyn services | `architecture.md` (§ Code Map) -> `backlog.md` |
| C# refactor or multi-file semantic change | `runtime.md` -> `bootstrap-read-tool-primer.md` -> `ai_docs/domains/tool-usage-guide.md` |
| Add or change a tool | `architecture.md` (§ Code Map) -> `ai_docs/references/mcp-server-best-practices.md` -> `ai_docs/references/testing.md` |
| Change error handling, tool-call dispatch, filters, or `Program.cs` | `ai_docs/references/mcp-server-best-practices.md` -> `architecture.md` (§ Code Map) |
| Evolve a DTO or contract | `architecture.md` (§ Code Map) |
| Where does a feature live in source | `architecture.md` (§ Code Map, § Entry points) |
| Write or update tests | `ai_docs/references/testing.md` -> `runtime.md` |
| Doc-only change | `../CI_POLICY.md` -> `workflow.md` |
| Human setup / Docker / CI artifacts | `../docs/setup.md` |
| Coverage baseline / CI artifacts | `../docs/coverage-baseline.md` -> `ai_docs/references/testing.md` |
| Experimental -> stable promotion review | `../docs/experimental-promotion-analysis.md` |
| Large-solution profiling method | `../docs/large-solution-profiling-baseline.md` |
| MCP deep-review audit session | `../skills/mcp-server-surface-test/prompts/full.md` -> `ai_docs/audit-reports/README.md` |
| Multi-repo MCP deep-review batch | `ai_docs/procedures/deep-review-program.md` -> `ai_docs/procedures/deep-review-command-reference.md` -> `../skills/mcp-server-surface-test/prompts/full.md` -> `ai_docs/audit-reports/README.md` -> `ai_docs/reports/README.md` |
