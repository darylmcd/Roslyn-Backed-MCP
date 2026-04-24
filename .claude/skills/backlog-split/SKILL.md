---
name: backlog-split
description: Split ai_docs/backlog.md into priority-scoped sibling files (ai_docs/backlog-p3.md, ai_docs/backlog-p4.md) when it breaches the doc-audit 4000-token hard cap. Use when /doc-audit reports "ai_docs/backlog.md over" in the Size Compliance table, or when the main backlog file grows past ~30 rows and hit-counts on the file start showing up as friction in transcripts. Keeps P2 inline; extracts P3 and P4 to their own files; updates the Refs table and Agent contract so routing still lands on the correct file. Preserves every row id verbatim so open-initiative references don't break.
disable-model-invocation: true
---

# backlog-split

Mechanical right-size of `ai_docs/backlog.md` when it exceeds the 4000-token hard cap.

## When to use

- `/doc-audit` reports `ai_docs/backlog.md` as `over` in the Size Compliance table.
- `.ai-doc-audit.md` sets `next_mode: initial` with rationale citing a backlog size breach.
- Main backlog file crosses ~30 rows and becomes unwieldy to read in one pass.

## Pre-flight

1. Working tree clean. Run `git status --porcelain`. If anything tracked is modified, stop and ask — a split rewrites the whole file and the diff must be reviewable alone.
2. Current file is well-formed: a single `updated_at` line, `## P2 — open work` / `## P3 — open work` / `## P4 — open work` section headers exist, one table per section with the canonical `| id | pri | deps | do |` header, and a trailing `## Refs` section.
3. Read the full `ai_docs/backlog.md` once; extract all row ids, their priority bands, and their line ranges.

## Split plan

Default output shape — adapt section names if the file already uses different labels:

- `ai_docs/backlog.md` — stays as the root entry point. Keeps:
  - YAML-ish metadata (`updated_at`, `## Agent contract`, `## Standing rules`).
  - `## P2 — open work` table (leave inline — P2 is the hot-work band; agents read it first).
  - A **new** `## Priority-scoped backlog files` subsection that routes to the split files.
  - `## Refs` table (augmented with the two new entries).
- `ai_docs/backlog-p3.md` (new) — contains the `## P3 — open work` section moved verbatim.
- `ai_docs/backlog-p4.md` (new) — contains the `## P4 — open work` section moved verbatim.

Both new files get:
- A `<!-- purpose: P{N} backlog rows, split from ai_docs/backlog.md on {ISO-date} -->` comment.
- A `<!-- scope: in-repo -->` tag immediately after (doc-audit §Planning-Doc-Scope-Rules).
- A one-line back-pointer referencing `ai_docs/backlog.md` as the root; note that contract and standing rules live there and this file holds only P{N} rows. Use a relative-link form appropriate to the file's location.
- The exact `| id | pri | deps | do |` header + every row that was in that section.
- No `Refs` table (lives in root only).

## Steps

1. Read `ai_docs/backlog.md` fully. Note the line ranges of each `## P{2,3,4} — open work` section.
2. Compute the new `ai_docs/backlog.md` body: keep metadata + P2 section + augmented Refs.
3. Write `ai_docs/backlog-p3.md` and `ai_docs/backlog-p4.md` with the extracted sections + pointer header.
4. In the root file, insert a new `## Priority-scoped backlog files` subsection right after `## Standing rules`. Its content is a small table with columns `File | Holds | When to read`, one row per split file (`backlog-p3.md`, `backlog-p4.md`) using the standard sibling-relative link form (target filename as both label and href), plus a one-line footer explaining that the root backlog holds P2 + contract + standing rules and that row ids never duplicate across files.

5. Append rows to the root `## Refs` table for each split file: the `Path` cell is the full repo-relative path in inline-code (e.g. `ai_docs/backlog-p3.md`) and the `Role` cell names the priority band plus the ISO split date.

6. Bump `**updated_at:**` to the current UTC ISO datetime.
7. Re-run the doc-audit size check: `wc -c ai_docs/backlog.md` ÷ 4 ≈ tokens. Should now be < 4000.
8. Verify `git status` shows exactly three modified/added files: `ai_docs/backlog.md`, `ai_docs/backlog-p3.md`, `ai_docs/backlog-p4.md`.

## Post-flight

- Grep for files that may hard-code `ai_docs/backlog.md` for `P3` / `P4` references and update routing where needed:
  - `ai_docs/planning_index.md` — router; update to mention split files.
  - `ai_docs/prompts/backlog-sweep-plan.md` + `backlog-sweep-execute.md` — planner/executor prompts. Make sure they read all three files.
  - `.claude/skills/close-backlog-rows/SKILL.md` — row-closer must resolve ids across all three files.
  - `AGENTS.md` — if it names `backlog.md` specifically, confirm the pointer is still accurate (pointing to the root file is correct post-split).

## Non-goals

- Do NOT archive or delete any rows. Split is a move, never a prune. If rows are stale, file a separate close-pass first.
- Do NOT merge priority bands. P2 / P3 / P4 stay distinct; they're priority signals, not routing breadcrumbs.
- Do NOT re-sort rows within a band. Preserve insertion order so row-age is still readable.

## Rollback

If a split produces an unreviewable diff, `git restore ai_docs/backlog*.md && git clean -f ai_docs/backlog-p3.md ai_docs/backlog-p4.md`. Working tree is the only state — no server or cache needs resetting.
