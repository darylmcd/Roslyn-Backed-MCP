# AI Docs Index

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
| `prompts/deep-review-and-refactor.md` | Living reusable prompt for comprehensive code review and MCP server audit (all 18 phases). Keep in sync with project surface. Do not delete. |
| `prompts/add-security-diagnostic-surface.prompt.md` | Feature specification for FEAT-01 |
| `prompts/add-nuget-vulnerability-surface.prompt.md` | Feature specification for FEAT-06 (NuGet vulnerability scanning) |

## Archive

| File | Purpose |
|------|---------|
| `archive/README.md` | Index of archived material |
| `archive/deep-review-report.md` | Point-in-time code review report (2026-03-30, v1.2.0) |
| `archive/mcp-server-audit-report.md` | Point-in-time MCP server audit (2026-03-30, 35 issues across 4 solutions) |

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
| Merge-ready handoff | `CI_POLICY.md` → `workflow.md` |
| Doc-only change | `CI_POLICY.md` (run `verify-ai-docs.ps1`) |
| Planning new features | `backlog.md` → `architecture.md` → `docs/roadmap.md` |
