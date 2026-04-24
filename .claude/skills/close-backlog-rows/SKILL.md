---
name: close-backlog-rows
description: "Close one or more rows in `ai_docs/backlog.md` by id — resolves each id to its single line, deletes atomically, bumps `updated_at`, and reports which P-band decremented. Use when: a batch reconcile or ship step needs to remove N already-shipped backlog rows in one pass, to avoid the per-row Grep + Read + Edit dance that hits the `[Omitted long matching line]` friction on backlog.md's 1500+ char rows."
user-invocable: true
argument-hint: "comma-separated row ids (e.g. `position-probe-for-test-fixture-authoring,pragma-directive-scope-manipulation`)"
---

# Close Backlog Rows

You close rows in `ai_docs/backlog.md` atomically by id. Non-destructive per-row verification, one `updated_at` bump at the end, structured per-band decrement report.

This skill does **not** query GitHub, pick the next rows to close, or edit any other file (state.json, plan.md, CHANGELOG.md, worktree state). It is a bookkeeping primitive for backlog closures.

## Why this exists

`ai_docs/backlog.md` stores each row as a single long markdown table line (1500+ chars). When an agent tries to verify a row's content via `Grep` before `Edit`-ing, the `[Omitted long matching line]` truncation makes the context view unusable. The workaround is `Read` at a specific `offset` + `limit: 1` per row — that costs 3 tool calls per row (Grep + Read + Edit) for a mechanical delete. Batch reconciles close 2-6 rows at a time; the overhead is meaningful.

This skill compresses the per-row dance into a single call.

## Input

`$ARGUMENTS` is a comma-separated list of row ids (kebab-case, matching the `id` field of rows in `ai_docs/backlog.md`). Whitespace around commas is tolerated. Ids must match existing rows exactly — the skill refuses on unknown ids rather than guess.

Example: `/close-backlog-rows position-probe-for-test-fixture-authoring, pragma-directive-scope-manipulation`

## Preconditions (HARD GATES — refuse if any fail)

1. **`ai_docs/backlog.md` exists** at the repo root. If missing, refuse: `"No ai_docs/backlog.md at repo root. Are you in the right repository?"`.
2. **At least one row id supplied** — empty `$ARGUMENTS` refuses with usage text.
3. **Working tree is clean on `ai_docs/backlog.md`** (no unstaged edits), OR the only unstaged path on it is the exact set of rows this skill is about to remove. If dirtier, refuse: `"ai_docs/backlog.md has unrelated unstaged edits — commit or stash before closing rows."`.
4. **Each supplied id resolves to exactly ONE row** — zero matches → refuse with the unknown id; two or more matches → refuse with the line numbers (indicates duplicate rows, a separate bug).

## Workflow

### Step 1 — Resolve ids to row lines

For each id in the input, grep for the row-start anchor:

```
^\| `<id>` \|
```

Record per id:
- The line number of the match (via `grep -n`).
- The full row text (read line-at-offset, `limit: 1`).
- The P-band field (2nd pipe-delimited cell — `P2` / `P3` / `P4`).

If any id returns zero matches OR two-or-more matches, STOP and emit the refusal per Precondition 4. Do not partially delete.

### Step 2 — Dry-run preview

Print the resolution table to the user:

```
Resolution for ai_docs/backlog.md:
  position-probe-for-test-fixture-authoring       @ line 61 (P4)
  pragma-directive-scope-manipulation             @ line 63 (P4)
  workspace-stale-after-external-edit-feedback    @ line 73 (P4)
Total: 3 rows → P4 band will decrement by 3.

Proceed? (skill will: Edit × 3 to remove rows, Edit × 1 to bump updated_at, then show the final diff).
```

In automated invocations (skill is called as part of a larger reconcile flow where the caller has already confirmed), skip the prompt and proceed directly.

### Step 3 — Delete each row

Use `Edit` with `replace_all: false` for each row, providing the exact full-line text as `old_string` and an empty `new_string`. Do all deletions before the `updated_at` bump — order within the batch does not matter because each `Edit` is line-scoped, but do them sequentially (not in parallel) so that mid-batch partial failure produces a coherent intermediate state.

### Step 4 — Bump `updated_at:`

The file has one line near the top matching:

```
**updated_at:** <ISO timestamp>
```

Replace the timestamp with the current UTC ISO timestamp (e.g. `2026-04-18T19:45:00Z`). Resolve "current" via a single `date -u +%Y-%m-%dT%H:%M:%SZ` call — do not hardcode from the prompt's today's-date metadata.

### Step 5 — Report

Emit the structured result:

```
Closed 3 rows in ai_docs/backlog.md:
  P4: position-probe-for-test-fixture-authoring, pragma-directive-scope-manipulation, workspace-stale-after-external-edit-feedback

P-band deltas:
  P2: 0
  P3: 0
  P4: -3

Post-close row counts (best-effort):
  P2: <n>
  P3: <n>
  P4: <n>

updated_at bumped → <new ISO>
```

Post-close row counts are best-effort — a subsequent `grep -c` on each band's table is cheap and helpful. If the count cannot be determined quickly, omit that block rather than block.

## Non-goals

- **Do NOT edit `CHANGELOG.md`, `changelog.d/*.md`, `state.json`, or `plan.md`.** Those are separate concerns: `/draft-changelog-entry` writes `changelog.d/<row-id>.md` fragments; `/reconcile-backlog-sweep-plan` updates state.json + plan.md; `CHANGELOG.md` is rewritten only at version-bump time by `/bump`.
- **Do NOT commit or push.** The caller (typically a reconcile PR) stages the change and opens the PR.
- **Do NOT query GitHub.** This skill operates purely on local files. `gh` is not required.
- **Do NOT re-order or re-format the `Refs` table at the bottom of backlog.md.** Row closure only touches the P2/P3/P4 open-work tables and the `updated_at:` line.

## Refusal cases (explicit)

- **Row not found** → refuse with the id and a one-line "try `grep -n \`<id>\`` to verify the id exists in ai_docs/backlog.md".
- **Row found twice** → refuse with the two line numbers; this signals a backlog-integrity bug a human should fix.
- **Working tree dirtier than "this file will touch"** → refuse per Precondition 3. Do not auto-stash.
- **Empty `$ARGUMENTS`** → print usage and exit without touching files.

## Example

Input: `/close-backlog-rows first-test-for-new-service-scaffold,di-lifetime-mismatch-detection`

```
Resolution for ai_docs/backlog.md:
  first-test-for-new-service-scaffold  @ line 58 (P4)
  di-lifetime-mismatch-detection       @ line 54 (P4)
Total: 2 rows → P4 band will decrement by 2.

Deleting row at line 58 (first-test-for-new-service-scaffold)... ok
Deleting row at line 54 (di-lifetime-mismatch-detection)... ok
Bumping updated_at → 2026-04-18T14:10:00Z... ok

Closed 2 rows in ai_docs/backlog.md:
  P4: first-test-for-new-service-scaffold, di-lifetime-mismatch-detection

P-band deltas:
  P2: 0
  P3: 0
  P4: -2
```

## Distinct from related skills

- **`/reconcile-backlog-sweep-plan`**: reconciles `state.json` + `plan.md` against `gh pr view` reality. Does NOT touch backlog.md. Use both in sequence when a batch reconcile needs plan-state sync + row closure.
- **`/draft-changelog-entry`**: writes ONE `changelog.d/<row-id>.md` fragment per PR. This skill closes the corresponding backlog row(s). In a batch reconcile PR, pair them: draft-changelog-entry per merged PR, then close-backlog-rows with the full id list.
- **`/ship`**: commits + pushes + opens PR + merges the current branch. Does NOT close backlog rows — callers close first, then ship.
