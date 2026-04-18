# AGENTS Bootstrap

Use this file as the stable AI bootstrap entry point for this repository.

Read these canonical files in order:

1. `CI_POLICY.md`
2. `ai_docs/README.md`
3. `ai_docs/workflow.md`
4. `ai_docs/runtime.md`
5. `ai_docs/bootstrap-read-tool-primer.md`
6. `ai_docs/backlog.md`
7. `.github/copilot-instructions.md`
8. `.cursor/rules/operational-essentials.md`
9. `CLAUDE.md`

Policy ownership:

- Git, branch, worktree, and PR behavior: `ai_docs/workflow.md`
- Validation and merge gating: `CI_POLICY.md`
- Runtime assumptions and execution context: `ai_docs/runtime.md`
- Roslyn MCP usage — **read-side tools are always preferred over Grep / Bash: dotnet
  build / Bash: dotnet test, including in bootstrap self-edit sessions** — see
  `ai_docs/bootstrap-read-tool-primer.md` for the canonical pattern→tool table and
  `ai_docs/runtime.md` § *Roslyn MCP client policy (AI sessions)* for the three-part
  policy (read-side / write-side / bootstrap scope). For the self-edit workflow
  worked example, see `ai_docs/runtime.md` § *Self-edit recipe*.
- Unfinished work tracking: `ai_docs/backlog.md` (contract: **Agent contract** section — unfinished only; sync on ship).
- Skill packaging: shipped skills live in `./skills/` (bundled by `plugin.json` and distributed to every installer); repo-only maintainer skills live in `.claude/skills/` (auto-discovered by Claude Code in this checkout, never shipped). `./skills/**/SKILL.md` must not reference `ai_docs/`, `state.json`, `schemaVersion`, `backlog-sweep`, `backlog.md`, `eng/`, `just verify-`, `Directory.Build.props`, or `BannedSymbols.txt` — GitHub URLs pointing at this repo's public docs are allowed. Enforced by `eng/verify-skills-are-generic.ps1` (run via `just verify-skills`; gates `just ci` and `verify-release.ps1`).

