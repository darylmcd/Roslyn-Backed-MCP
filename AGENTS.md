# AGENTS Bootstrap

Use this file as the stable AI bootstrap entry point for this repository.

## Canonical Rule Sources

- Validation and merge gating: `CI_POLICY.md`
- AI-doc routing and task-specific reads: `ai_docs/README.md`
- Git, branch, worktree, and PR workflow: `ai_docs/workflow.md`
- Runtime assumptions, runner commands, and MCP client policy: `ai_docs/runtime.md`
- Read-side Roslyn MCP bootstrap discipline: `ai_docs/bootstrap-read-tool-primer.md`
- Planning and unfinished work routing: `ai_docs/planning_index.md`, `ai_docs/backlog.md`
- Implementation quality and safety: `.github/copilot-instructions.md`
- Cursor reminder layer: `.cursor/rules/operational-essentials.md`
- Skill packaging: shipped skills live in `./skills/` (bundled by `plugin.json` and distributed to every installer); repo-only maintainer skills live in `.claude/skills/` (auto-discovered by Claude Code in this checkout, never shipped). `./skills/**/SKILL.md` must not reference `ai_docs/`, `state.json`, `schemaVersion`, `backlog-sweep`, `backlog.md`, `eng/`, `just verify-`, `Directory.Build.props`, or `BannedSymbols.txt` — GitHub URLs pointing at this repo's public docs are allowed. Enforced by `eng/verify-skills-are-generic.ps1` (run via `just verify-skills`; gates `just ci` and `verify-release.ps1`).
- Third-party attribution, only when packaging or legal-notice work touches shipped artifacts: `THIRD-PARTY-NOTICES.md`

## Session Start (Required)

Read these files in order before doing work:

1. `CI_POLICY.md`
2. `ai_docs/README.md`
3. `ai_docs/workflow.md`
4. `ai_docs/runtime.md`
5. `ai_docs/bootstrap-read-tool-primer.md`
6. `ai_docs/backlog.md`
7. `.github/copilot-instructions.md`
8. `.cursor/rules/operational-essentials.md`

After the required reads, use `ai_docs/planning_index.md` for next-step routing and `ai_docs/README.md` for task-specific documents.

## MCP Bootstrap

- Read `.mcp.json` after the required session-start files and before task-specific tool decisions.
- Treat `.mcp.json` as the repository's declared MCP intent, not as proof that a server is live in the current session.
- Compare the declared servers in `.mcp.json` with the live MCP/tool surface actually exposed by the host.
- When reporting MCP availability, distinguish clearly between what is declared in `.mcp.json`, what is documented in `ai_docs/runtime.md`, and what is verified live in the current session.
- If a server is declared in `.mcp.json` but unavailable at runtime, say so explicitly and continue with the best supported fallback.

## Planning Scope

1. User named no specific repo / adapter / ecosystem / integration / cross-repo term -> scope = in-repo -> read `backlog.md`, then any named in-repo file under `ai_docs/plans/` -> STOP. Do not open `ai_docs/ecosystem/**`.
2. User named another repo / adapter / ecosystem / integration / cross-repo work -> scope = cross-project -> this repo has no local `ai_docs/ecosystem/**`; say so explicitly and use only the external context the user named.
3. Both scopes named -> answer each as a separate question; do not merge into one recommendation.

## Conflict Precedence

- For implementation quality and safety conflicts, follow `.github/copilot-instructions.md`.
- For workflow and collaboration conflicts, follow `ai_docs/workflow.md`.
- For validation and merge-gating conflicts, follow `CI_POLICY.md`.
- For runtime, runner, or MCP-client-policy conflicts, follow `ai_docs/runtime.md`.
