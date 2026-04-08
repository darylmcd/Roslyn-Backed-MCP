# AI Docs Index

<!-- purpose: Route agents to canonical AI docs; index only—no embedded policy prose. -->

This directory is the canonical AI-facing documentation tree. Read this file to find what to load for your task.

## Core References (read on every session)

| File | Purpose |
|------|---------|
| `../CI_POLICY.md` | Validation and merge-gating policy |
| `workflow.md` | Git/branch/worktree/PR workflow |
| `runtime.md` | Build, test, run commands; execution context; **Roslyn MCP client policy** (use server for C# refactoring, not discovery-only) |
| `backlog.md` | Open work only; **sync when closing items** (same PR or immediate follow-up). See § Agent contract. |
| `architecture.md` | System layers, data flow, key abstractions |

## Domain Entry Points (read when touching that layer)

| File | Covers |
|------|--------|
| `domains/host-stdio/reference.md` | MCP host, tool wiring, protocol logging |
| `domains/core-contracts/reference.md` | DTOs, request/response contracts |
| `domains/roslyn-services/reference.md` | Workspace, semantic navigation, analysis, refactoring |
| `domains/tool-usage-guide.md` | How to choose the right tools and workflows |

## Reference Material (read on demand)

| File | Purpose |
|------|---------|
| `references/testing.md` | Test patterns, test command, coverage guidance |
| `references/tooling/dotnet.md` | dotnet CLI commands used in this repo |
| `references/tooling/mcp-clients.md` | MCP client integration notes |

## Repeatable Procedures & Prompts

| File | Purpose |
|------|---------|
| `procedures/doc-migration-checklist.md` | Checklist for documentation migrations |
| `procedures/deep-review-program.md` | Multi-repo deep-review matrix, raw-vs-rollup outputs, and backlog intake rules |
| `procedures/deep-review-command-reference.md` | Example shell commands for import, rollup, and one-command batch deep-review workflows |
| `prompts/standardize-documentation.md` | Cross-repo prompt: run `/doc-audit` first, then align human + AI docs, packaging/install inventory, stale ref removal |
| `prompts/standardize-backlog-hygiene.md` | Align backlog hygiene across repos with `backlog.md` and `workflow.md` |
| `prompts/deep-review-and-refactor.md` | Living reusable prompt for comprehensive MCP server audit, experimental→stable promotion scoring, and plugin-skill verification (Phases 0–18 + 16b). Keep in sync with project surface. Do not delete. |
| `prompts/test-suite-audit.md` | Audit tests for performance smells, workspace/init issues, SRP, and tight focus—suite should not introduce slowness or instability |
| `audit-reports/README.md` | Where raw MCP deep-review audit outputs live; links session worksheet and rollup handoff |
| `audit-reports/deep-review-session-checklist.md` | Optional fill-in checklist for a deep-review session (aligned with the living prompt, including promotion scorecard + plugin skills lane) |
| `reports/README.md` | Where synthesized rollups and cross-cutting audit reports live |
| `reports/2026-04-06-deep-review-rollup-example.md` | Concrete example of a synthesized deep-review rollup for multi-repo batches |
| `reports/2026-04-06-test-suite-audit.md` | Example cross-cutting audit report for test-suite review |

## Archive

| File | Purpose |
|------|---------|
| `archive/README.md` | Policy for archived material; no other markdown files are tracked in this folder |

---

## Task-Scoped Reading Guide

| Task | Files to read |
|------|--------------|
| First session / orientation | `AGENTS.md` → `CI_POLICY.md` → `workflow.md` → `runtime.md` → `architecture.md` |
| Fix a bug in Roslyn services | `architecture.md` → `domains/roslyn-services/reference.md` → `backlog.md` |
| C# refactor or multi-file semantic change | `runtime.md` (Roslyn MCP client policy) → `domains/tool-usage-guide.md` |
| Add or change a tool | `domains/host-stdio/reference.md` → `domains/roslyn-services/reference.md` → `references/testing.md` |
| Evolve a DTO or contract | `domains/core-contracts/reference.md` → `architecture.md` |
| Write or update tests | `references/testing.md` → `runtime.md` |
| Audit test suite (performance, SRP, workspace/init smells) | `prompts/test-suite-audit.md` → `references/testing.md` |
| Merge-ready handoff | `CI_POLICY.md` → `workflow.md` |
| Doc-only change | `CI_POLICY.md` (run `verify-ai-docs.ps1`) |
| Planning new features | `backlog.md` → `architecture.md` → `docs/roadmap.md` |
| Human setup / Docker / CI artifacts | `docs/setup.md` |
| Claude Code plugin / skills / hooks | `README.md` § *Claude Code Plugin Installation* → `docs/setup.md` § *Claude Code Plugin* |
| Release parity / must-have matrix | `docs/parity-gap-implementation-plan.md` |
| Coverage baseline / CI artifacts | `docs/coverage-baseline.md` → `references/testing.md` |
| Experimental → stable promotion review | `docs/experimental-promotion-analysis.md` |
| Large-solution profiling method | `docs/large-solution-profiling-baseline.md` |
| MCP deep-review audit session | `prompts/deep-review-and-refactor.md` → `audit-reports/README.md` |
| Multi-repo MCP deep-review batch | `procedures/deep-review-program.md` → `procedures/deep-review-command-reference.md` → `prompts/deep-review-and-refactor.md` → `audit-reports/README.md` → `reports/README.md` |
