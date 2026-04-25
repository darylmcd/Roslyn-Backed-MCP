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
| `domains/host-stdio/reference.md` | MCP host, tool wiring, protocol logging |
| `domains/core-contracts/reference.md` | DTOs, request/response contracts |
| `domains/roslyn-services/reference.md` | Workspace, semantic navigation, analysis, refactoring |
| `domains/tool-usage-guide.md` | How to choose the right tools and verify changes |

## Reference Material (read on demand)

| File | Purpose |
|------|---------|
| `references/testing.md` | Test patterns, commands, and coverage guidance |
| `references/tooling/dotnet.md` | dotnet CLI commands used in this repo |
| `references/tooling/mcp-clients.md` | MCP client integration notes |
| `references/mcp-server-best-practices.md` | MCP error-model, filter pipeline, and protocol hygiene guidance |

## Planning And Active Work

| File | Purpose |
|------|---------|
| `plans/20260422T170500Z_test-parallelization-audit/plan.md` | In-repo phased plan for the deferred test-parallelization audit |

## Procedures And Prompts

| File | Purpose |
|------|---------|
| `procedures/doc-migration-checklist.md` | Checklist for documentation migrations |
| `procedures/deep-review-program.md` | Multi-repo deep-review matrix, raw-vs-rollup outputs, and backlog intake rules |
| `procedures/deep-review-backlog-intake.md` | Reference procedure for merging deep-review findings back into `backlog.md` |
| `procedures/deep-review-command-reference.md` | Shell commands for import, rollup, compare, and batch review workflows |
| `procedures/audit-21-implementation-plan.md` | In-repo implementation plan for AUDIT-21 |
| `prompts/backlog-sweep-plan.md` | Planner prompt for batching backlog rows into shippable initiatives |
| `prompts/backlog-sweep-execute.md` | Executor prompt for shipping the next pending initiative |
| `prompts/standardize-documentation.md` | Cross-repo prompt for doc-audit-driven documentation cleanup |
| `prompts/standardize-backlog-hygiene.md` | Reference prompt for backlog/workflow hygiene alignment |
| `prompts/deep-review-and-refactor.md` | Living prompt for comprehensive MCP deep-review and promotion scoring |
| `prompts/stress-test-external-repo.md` | Performance and correctness stress-test protocol for large external solutions |
| `prompts/roslyn-mcp-multisession-retro.md` | Cross-repo retrospective prompt that scans Claude Code session transcripts for Roslyn MCP issues, missing-tool gaps, and recommendations |

## Reports And Archive

| File | Purpose |
|------|---------|
| `audit-reports/README.md` | Raw MCP audit outputs and `audit-reports/deep-review-session-checklist.md` |
| `reports/README.md` | Synthesized rollups and cross-cutting audit reports |
| `archive/README.md` | Archive policy |

---

## Task-Scoped Reading Guide

| Task | Files to read |
|------|---------------|
| First session / orientation | `AGENTS.md` -> `../CI_POLICY.md` -> `workflow.md` -> `runtime.md` -> `architecture.md` |
| Planning or "what next?" in this repo | `planning_index.md` -> `backlog.md` -> relevant file under `plans/` |
| Fix a bug in Roslyn services | `architecture.md` -> `domains/roslyn-services/reference.md` -> `backlog.md` |
| C# refactor or multi-file semantic change | `runtime.md` -> `bootstrap-read-tool-primer.md` -> `domains/tool-usage-guide.md` |
| Add or change a tool | `domains/host-stdio/reference.md` -> `references/mcp-server-best-practices.md` -> `domains/roslyn-services/reference.md` -> `references/testing.md` |
| Change error handling, tool-call dispatch, filters, or `Program.cs` | `references/mcp-server-best-practices.md` -> `domains/host-stdio/reference.md` |
| Evolve a DTO or contract | `domains/core-contracts/reference.md` -> `architecture.md` |
| Write or update tests | `references/testing.md` -> `runtime.md` |
| Doc-only change | `../CI_POLICY.md` -> `workflow.md` |
| Human setup / Docker / CI artifacts | `../docs/setup.md` |
| Coverage baseline / CI artifacts | `../docs/coverage-baseline.md` -> `references/testing.md` |
| Experimental -> stable promotion review | `../docs/experimental-promotion-analysis.md` |
| Large-solution profiling method | `../docs/large-solution-profiling-baseline.md` |
| MCP deep-review audit session | `prompts/deep-review-and-refactor.md` -> `audit-reports/README.md` |
| Multi-repo MCP deep-review batch | `procedures/deep-review-program.md` -> `procedures/deep-review-command-reference.md` -> `prompts/deep-review-and-refactor.md` -> `audit-reports/README.md` -> `reports/README.md` |
