**Sync rule:** This file mirrors AGENTS.md. At the start of every session, check if AGENTS.md has changed. If it has, update this file to match, preserving this sync rule block.

---

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
