# AI Docs Index

<!-- purpose: Route agents to canonical AI docs; index only — no embedded policy prose. -->

This directory is the canonical AI-facing documentation tree. Use this file to find what to load for a task.

## Core References (read on every session)

| File | Purpose |
|------|---------|
| `../CI_POLICY.md` | Validation and merge-gating policy |
| `workflow.md` | Git/branch/worktree/PR workflow |
| `runtime.md` | Build, test, run commands; execution context; Roslyn MCP client policy |
| `bootstrap-read-tool-primer.md` | Canonical pattern-to-tool cheat sheet for read-side MCP usage |
| `planning_index.md` | Router for in-repo planning docs and scope boundaries |
| `backlog.md` | Open work only; sync when closing rows |
| `architecture.md` | System layers, data flow, and key abstractions |

## Domain Entry Points (read when touching that layer)

| File | Covers |
|------|--------|
| `ai_docs/domains/host-stdio/reference.md` | MCP host, tool wiring, protocol logging |
| `ai_docs/domains/core-contracts/reference.md` | DTOs, request/response contracts |
| `ai_docs/domains/roslyn-services/reference.md` | Workspace, semantic navigation, analysis, refactoring |
| `ai_docs/domains/tool-usage-guide.md` | How to choose the right tools and verify changes |

## Reference Material (read on demand)

| File | Purpose |
|------|---------|
| `ai_docs/references/testing.md` | Test patterns, commands, and coverage guidance |
| `ai_docs/references/tooling/dotnet.md` | dotnet CLI commands used in this repo |
| `ai_docs/references/tooling/mcp-clients.md` | MCP client integration notes |
| `ai_docs/references/mcp-server-best-practices.md` | MCP error-model, filter pipeline, and protocol hygiene guidance |

## Procedures And Prompts

| File | Purpose |
|------|---------|
| `ai_docs/procedures/doc-migration-checklist.md` | Checklist for documentation migrations |
| `ai_docs/procedures/deep-review-program.md` | Multi-repo deep-review matrix, raw-vs-rollup outputs, and backlog intake rules |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Reference procedure for merging deep-review findings back into `backlog.md` |
| `ai_docs/procedures/deep-review-command-reference.md` | Shell commands for import, rollup, compare, and batch review workflows |
| `ai_docs/procedures/audit-21-implementation-plan.md` | In-repo implementation plan for AUDIT-21 |
| `ai_docs/prompts/profile-large-solution.md` | Runbook for collecting 50+ project Roslyn MCP profiling evidence |
| `ai_docs/prompts/standardize-documentation.md` | Cross-repo prompt for doc-audit-driven documentation cleanup |
| `ai_docs/prompts/standardize-backlog-hygiene.md` | Reference prompt for backlog/workflow hygiene alignment |
| `ai_docs/prompts/stress-test-external-repo.md` | Performance and correctness stress-test protocol for large external solutions |
| `ai_docs/prompts/roslyn-mcp-multisession-retro.md` | Cross-repo retrospective prompt that scans Claude Code session transcripts for Roslyn MCP issues, missing-tool gaps, and recommendations |

## Reports And Archive

| File | Purpose |
|------|---------|
| `ai_docs/audit-reports/README.md` | Raw MCP audit outputs and `audit-reports/deep-review-session-checklist.md` |
| `ai_docs/reports/README.md` | Synthesized rollups and cross-cutting audit reports |
| `ai_docs/archive/README.md` | Archive policy |

---

## Task-Scoped Reading Guide

| Task | Files to read |
|------|---------------|
| First session / orientation | `AGENTS.md` -> `../CI_POLICY.md` -> `workflow.md` -> `runtime.md` -> `architecture.md` |
| Planning or "what next?" in this repo | `planning_index.md` -> `backlog.md` |
| Fix a bug in Roslyn services | `architecture.md` -> `ai_docs/domains/roslyn-services/reference.md` -> `backlog.md` |
| C# refactor or multi-file semantic change | `runtime.md` -> `bootstrap-read-tool-primer.md` -> `ai_docs/domains/tool-usage-guide.md` |
| Add or change a tool | `ai_docs/domains/host-stdio/reference.md` -> `ai_docs/references/mcp-server-best-practices.md` -> `ai_docs/domains/roslyn-services/reference.md` -> `ai_docs/references/testing.md` |
| Change error handling, tool-call dispatch, filters, or `Program.cs` | `ai_docs/references/mcp-server-best-practices.md` -> `ai_docs/domains/host-stdio/reference.md` |
| Evolve a DTO or contract | `ai_docs/domains/core-contracts/reference.md` -> `architecture.md` |
| Write or update tests | `ai_docs/references/testing.md` -> `runtime.md` |
| Doc-only change | `../CI_POLICY.md` -> `workflow.md` |
| Human setup / Docker / CI artifacts | `../docs/setup.md` |
| Coverage baseline / CI artifacts | `../docs/coverage-baseline.md` -> `ai_docs/references/testing.md` |
| Experimental -> stable promotion review | `../docs/experimental-promotion-analysis.md` |
| Large-solution profiling method | `../docs/large-solution-profiling-baseline.md` |
| MCP deep-review audit session | `../skills/mcp-server-surface-test/prompts/full.md` -> `ai_docs/audit-reports/README.md` |
| Multi-repo MCP deep-review batch | `ai_docs/procedures/deep-review-program.md` -> `ai_docs/procedures/deep-review-command-reference.md` -> `../skills/mcp-server-surface-test/prompts/full.md` -> `ai_docs/audit-reports/README.md` -> `ai_docs/reports/README.md` |
