# Prompt: Standardize backlog hygiene + planner closure

<!-- purpose: Cross-repo prompt to keep backlog.md and workflow.md aligned with open-work-only rules. -->

Portable prompt for Cursor, Claude, or other planners. **Canonical paths are fixed:** `ai_docs/backlog.md` and `ai_docs/workflow.md` (same layout across your repos). Copy the block below as-is.

---

## Full prompt (copy from the block below)

```
You are standardizing how this repository tracks unfinished work and how implementation plans close the loop.

Goals:
1. **Backlog file** (`ai_docs/backlog.md`) must list **only open work**, not changelogs. Optimize for agents: stable `id` per row, compact tables (`id | blocker | deps | do`), an **Agent contract** section at the top with MUST/MUST NOT rules.
2. **Workflow doc** (`ai_docs/workflow.md`) must add a short section stating: when work closes backlog rows, update the backlog in the **same PR** (or immediate follow-up); **implementation plans must include a final todo** such as `backlog: sync ai_docs/backlog.md`.
3. **Index** (if the repo has a doc index README) — one line linking the backlog and noting “sync after closing items.”

Tasks:
- Add or refactor `ai_docs/backlog.md` to include:
  - `## Agent contract` with: scope (unfinished only), MUST remove/update rows when work ships, MUST add planner final step `backlog: sync ai_docs/backlog.md`, MUST NOT add completed-history sections.
  - `updated_at` (or equivalent) for freshness.
  - Open items with **stable ids** (kebab-case, grep-friendly).
  - Separate **standing rules** (ongoing practices) from **deletable backlog rows** so agents don’t confuse them.
  - A minimal **Refs** section with canonical paths only.
- Add the **Backlog closure** subsection to `ai_docs/workflow.md` (near other doc-maintenance rules). Elsewhere in `ai_docs/workflow.md`, align structure with agent use (consistent `##` / table patterns, trim redundant prose) **without** dropping required policy content.
- Remove any “Completed” tables from the backlog (history belongs in git).
- Do not change application code unless required for docs.

Constraints:
- **Audience:** Refactor `ai_docs/backlog.md` and `ai_docs/workflow.md` for **agent and planner consumption** (stable headings, tables, short bullets, grep-friendly paths and ids). **Human-readable narrative is secondary** where it conflicts with that goal.
- Keep prose minimal; optimize for agents and planners.
- If the repo uses `AGENTS.md` / bootstrap docs, add a **one-line** pointer to the backlog contract if missing.

Deliverables:
- List of files changed.
- Short summary of the agent contract bullets for the user.
```

---

## Short variant (quick pass)

```
Add an **Agent contract** to `ai_docs/backlog.md`: unfinished work only; MUST sync/remove rows when items close; MUST NOT use backlog as changelog; implementation plans MUST end with `backlog: sync ai_docs/backlog.md`. Refactor open items to stable `id` columns. Refactor both `ai_docs/backlog.md` and `ai_docs/workflow.md` for agent/planner use (tables, short bullets, grep-friendly structure); narrative polish is secondary. Add **Backlog closure** to `ai_docs/workflow.md` and remove completed-history sections from the backlog.
```

---

## Optional context line (prepend when needed)

```
Repo root: <path>. Default branch: <main|master>. Doc style: Markdown tables OK.
```
