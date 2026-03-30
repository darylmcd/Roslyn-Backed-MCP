# AI Docs Index

This directory is the canonical AI-facing documentation tree.

## Bootstrap Read Order

1. `../AGENTS.md`
2. `../CI_POLICY.md`
3. `workflow.md`
4. `runtime.md`
5. `backlog.md`
6. `../.cursor/rules/operational-essentials.md`

## Active Current-State Docs

- `workflow.md`: canonical git/branch/worktree/PR workflow guidance.
- `runtime.md`: canonical runtime assumptions and execution context.
- `backlog.md`: only place for unfinished work items.
- `architecture.md`: compact system architecture and boundaries.
- `../CI_POLICY.md`: canonical validation and merge-gating policy.

## Domain Entry Points

- `domains/host-stdio/reference.md`
- `domains/core-contracts/reference.md`
- `domains/roslyn-services/reference.md`

## Active Reports

- `mcp-server-audit-report.md`: living bug reference for MCP server issues (29 issues across 3 solutions). Action items tracked in `backlog.md`.

## Stable Deep References

- `references/testing.md`
- `references/tooling/dotnet.md`
- `references/tooling/mcp-clients.md`
- `domains/tool-usage-guide.md`: help agents choose right tools and workflows

## Repeatable Procedures & Prompts

- `procedures/doc-migration-checklist.md`
- `prompts/deep-review-and-refactor.md`: reusable agent prompt for comprehensive code review
- `prompts/add-security-diagnostic-surface.prompt.md`: feature specification (tracked as FEAT-01 in backlog)

## Archive

- `archive/README.md`

Archive contains deep audits, historical investigations, point-in-time analyses, and superseded documents.
