---
name: reconcile-backlog-sweep-plan
description: "Reconcile a backlog-sweep plan's state.json + plan.md against merged/closed PR reality. Use when: multiple backlog-sweep PRs have landed and the plan still shows them as in-review/in-progress, or before picking the next pending initiative to avoid re-shipping already-merged work. Queries `gh pr view` per initiative, applies merged/deferred transitions, mirrors status into plan.md's table, and commits on a short-lived branch + PR (main is branch-protected). Automates Step 1b of `ai_docs/prompts/backlog-sweep-execute.md`."
user-invocable: true
argument-hint: "[plan-dir path] — defaults to newest ai_docs/plans/*_backlog-sweep/"
---

# Reconcile Backlog-Sweep Plan

You are a plan-state reconciler. Your job is to walk a backlog-sweep plan's `state.json` and `plan.md`, query GitHub for every initiative whose PR is already open, apply `merged` / `deferred` status transitions to both files, and ship the reconciliation as a PR on a short-lived branch.

This skill does **not** pick new work and does **not** execute initiatives. It is a bookkeeping pass that runs between PR waves.

## Server discovery

This skill edits repository files and shells out to `gh` + `git`. Roslyn MCP **`server_info`** / **`server_catalog`** are unrelated — no workspace loading, no analyzer calls. If you think you need Roslyn MCP, you are in the wrong skill.

## Input

`$ARGUMENTS` optionally names the plan directory (relative to repo root, e.g. `ai_docs/plans/20260417T120000Z_backlog-sweep`). If omitted, auto-select the newest directory matching `ai_docs/plans/*_backlog-sweep/` by timestamp-prefix sort descending.

## Preconditions (HARD GATES — refuse if any fail)

1. **`state.json` exists** at `{plan-dir}/state.json`. If missing, refuse: `"No state.json at {path}. Is this a backlog-sweep plan directory?"`.
2. **`plan.md` exists** at `{plan-dir}/plan.md`. If missing, refuse.
3. **`schemaVersion == 2`** in `state.json`. If older (or missing), refuse: `"Plan uses schemaVersion {n}; this skill supports 2 only. Re-run backlog-sweep-plan.md to regenerate."`.
4. **`gh` CLI is on PATH** (`gh --version` exits 0). If not, refuse: `"gh CLI not available — cannot query PR state. Install gh or run Step 1b manually."`.
5. **`git` CLI is on PATH** (`git --version` exits 0). If not, refuse.
6. **Working tree is clean** (`git status --porcelain` is empty) OR the only dirty paths are the two files this skill will edit (`state.json` + `plan.md`). If dirtier, refuse: `"Working tree has unrelated changes — commit or stash before reconciling."`.
7. **`main` is the default branch** and up to date with `origin/main`. Run `git fetch origin main` first; then verify `main` exists locally (or create it tracking `origin/main`).

## Workflow

### Step 1 — Locate plan and snapshot current state

1. Resolve the plan directory per Input above.
2. Read `{plan-dir}/state.json` into memory. Validate `schemaVersion == 2`.
3. Read `{plan-dir}/plan.md` into memory.
4. Build the **reconciliation candidate list**: every initiative whose `status` is `in-review` or `in-progress` AND whose `prUrl` is non-null. Skip `pending`, `merged`, `obsolete`, `deferred`.
5. If the candidate list is empty, report `"Nothing to reconcile — no in-review/in-progress initiatives with a PR URL."` and exit **without** creating a branch or PR.

### Step 2 — Query GitHub for each candidate

For each candidate, run:

```bash
gh pr view <prUrl> --json number,state,mergedAt,mergeCommit,closedAt
```

Parse the JSON. Tolerate a single-retry on transient `gh` failure (network / rate limit). If `gh pr view` fails twice, record the initiative id + error in a failure list; do NOT flip its status; continue with the rest.

Compute the intended transition per the rules from `ai_docs/prompts/backlog-sweep-execute.md` § Step 1b:

| Observed `state` | `mergedAt` | New `status` | Notes field update |
|---|---|---|---|
| `MERGED` | non-null ISO | `merged`; `mergedAt = <ISO>` | unchanged |
| `CLOSED` | null (not merged) | `deferred` | prepend `"PR #<n> closed without merge — manual triage required"` |
| `OPEN` | null | **no change** — leave `in-review` / `in-progress` | unchanged |

Record every transition you plan to make in a structured diff report (initiative id, old status → new status, PR number, merged/closed timestamp). Do not apply yet.

### Step 3 — Dry-run preview

Print the full transition plan to the user:

```
Reconciliation plan for {plan-dir}:
  {initiative-id-1}: in-review → merged (PR #{n}, merged {ISO})
  {initiative-id-2}: in-progress → deferred (PR #{n} closed without merge)
  {initiative-id-3}: in-review → (no change, PR still OPEN)
  ...
Skipped / errored:
  {initiative-id-X}: gh pr view failed — {error}
Total transitions: {k} of {candidate-count} candidates.
```

If `k == 0` after filtering no-change + errored rows, report `"No status transitions needed — every queried PR is still OPEN."` and exit without creating a branch or PR.

### Step 4 — Create a short-lived branch

`main` is branch-protected in this repo; commits must land via PR. Pick a branch name:

```
chore/reconcile-{plan-timestamp}-{YYYYMMDDHHMMSS}
```

where `{plan-timestamp}` is the `20260417T120000Z`-style prefix of the plan directory name and `{YYYYMMDDHHMMSS}` is UTC now. Example: `chore/reconcile-20260417T120000Z-20260417163015`.

```bash
git switch -c {branch-name} main
```

### Step 5 — Apply edits to state.json and plan.md

For each in-flight transition:

1. **Update state.json**:
   - Set `initiatives[i].status` to the new value.
   - For `merged`: set `mergedAt` to the GitHub-reported ISO string (not "now").
   - For `deferred`: append the "PR #<n> closed without merge — manual triage required" note to `notes` (separator `" | "` if notes already non-empty).
   - Leave other fields (branch, worktreePath, prUrl, rowsClosedCount, …) untouched.

2. **Update plan.md's Status row** for this initiative. The initiative's header block in `plan.md` looks like:
   ```
   ### {order}. `{initiative-id}` — {title}

   **Status:** {status} · **Order:** {order} · **Correctness class:** {class} …
   ```
   Rewrite the `**Status:** …` value:
   - `merged` → `merged (PR #{n}, {YYYY-MM-DD from mergedAt})`
   - `deferred` → `deferred (PR #{n} closed; see notes)`
   - Do not touch Order / Correctness class / Schedule hint / Estimated context / CHANGELOG category.

Write both files back. Keep JSON formatting stable (2-space indent, trailing newline if the original had one).

### Step 6 — Commit

Stage the two files explicitly (never `git add .` or `git add -A`):

```bash
git add {plan-dir}/state.json {plan-dir}/plan.md
```

Commit with a descriptive message citing the transitions. Template:

```
chore(plan): reconcile {plan-timestamp} state — {merged-count} merged, {deferred-count} deferred

Step 1b reconciliation for backlog-sweep plan {plan-timestamp}.

Transitions:
  - {id}: in-review → merged (PR #{n})
  - {id}: in-progress → deferred (PR #{n} closed)
  ...

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
```

Use a HEREDOC for the `-m` payload so multi-line formatting survives.

### Step 7 — Push and open PR

```bash
git push -u origin {branch-name}
gh pr create --title "chore(plan): reconcile {plan-timestamp} state" \
             --body "$(cat <<'EOF'
## Summary
- Step 1b reconciliation for `ai_docs/plans/{plan-dir}/`.
- {merged-count} initiatives transitioned to `merged`, {deferred-count} to `deferred`.
- Source of truth: `gh pr view` per PR at reconciliation time.

## Transitions
{bulleted list of id: old → new (PR #n, timestamp)}

## Test plan
- [x] `state.json` `schemaVersion` still 2
- [x] Only status / mergedAt / notes fields changed per initiative
- [x] `plan.md` Status rows mirror state.json
- [x] No unrelated files modified
EOF
)"
```

### Step 8 — Merge

Default: squash-merge with branch deletion.

```bash
gh pr merge <num> --squash --delete-branch
```

If the merge is rejected (most likely: branch needs rebase onto newer `main` because another PR landed during the window):

1. `git fetch origin main`
2. `git rebase origin/main`
3. Resolve any conflicts in `state.json` / `plan.md` — prefer **last-writer-wins on the same initiative row, keep both when rows differ**.
4. `git push --force-with-lease` (NOT plain `--force`).
5. Retry `gh pr merge <num> --squash --delete-branch`.

### Step 9 — Report

Emit a final structured summary:

```
Reconciliation complete.
  Plan: {plan-dir}
  Branch: {branch-name} (deleted)
  PR: {url} (merged as <sha>)
  Transitions applied:
    merged: {k}
    deferred: {j}
  Skipped (still OPEN): {o}
  Errored (gh pr view failed): {e} — {ids}
  Post-reconciliation status buckets: {pending-count} pending, {in-review-count} in-review, {merged-count} merged, {obsolete-count} obsolete, {deferred-count} deferred.
```

If all initiatives are now terminal (`merged` / `obsolete` / `deferred`), additionally note: `"Plan fully shipped — run backlog-sweep-execute.md Step 1b completion branch (marks completed: true + adds Refs entry) or prompt the user."` — do NOT do that completion step yourself; that is the executor's job.

## Refusal cases (explicit)

- **Plan directory missing / wrong shape** → refuse per Preconditions 1-2.
- **`schemaVersion != 2`** → refuse per Precondition 3 with re-plan instruction.
- **`gh` / `git` missing** → refuse per Preconditions 4-5.
- **Dirty working tree with unrelated paths** → refuse per Precondition 6.
- **`main` diverged in a way that requires manual merge** during Step 8 rebase and the rebase surfaces conflicts outside `state.json` / `plan.md` → stop and hand off to the user; do not force-push away another contributor's edits.
- **`gh pr view` fails for >50% of candidates** → stop before Step 4; report the failure rate and suggest re-running after the transient issue resolves.

## Example output

Input: `/reconcile-backlog-sweep-plan ai_docs/plans/20260417T120000Z_backlog-sweep`

```
Reconciliation plan for ai_docs/plans/20260417T120000Z_backlog-sweep:
  mcp-connection-session-resilience: in-review → merged (PR #218, merged 2026-04-17T13:05:19Z)
  mcp-server-surface-catalog-parity-generator: in-review → merged (PR #220, merged 2026-04-17T13:50:00Z)
  observation-rows-obsoletion-sweep: in-review → merged (PR #221, merged 2026-04-17T15:35:00Z)
Total transitions: 3 of 3 candidates.

Proceeding to Step 4 (branch) → Step 8 (merge).

Branch: chore/reconcile-20260417T120000Z-20260417163015
Commit: a1b2c3d chore(plan): reconcile 20260417T120000Z state — 3 merged, 0 deferred
PR #223 opened, merged as f9e8d7c6.

Reconciliation complete.
  Post-reconciliation status buckets: 34 pending, 0 in-review, 3 merged, 0 obsolete, 0 deferred.
```

## Why not commit directly on main

The executor prompt historically said "commit both edits on main". This repo has main branch protection enabled (see repo settings + recent PR history showing every merge goes through PR review). A direct push to main will be rejected at the server with `remote rejected … protected branch hook declined`. The short-lived branch + squash-merge pattern preserves the executor's intent (reconciliation lands on main) while respecting the branch-protection gate. When squash-merge lands, the commit message on main is identical to a direct commit would have been.

## Distinct from related skills

- **`/ship`**: ships the working branch's PR-scope changes. This skill runs on main after `/ship`-style PRs have merged, to mirror GitHub-observed state back into the plan files. Do not confuse: `/ship` runs per initiative; `/reconcile-backlog-sweep-plan` runs between initiative waves.
- **`backlog-sweep-execute.md` Step 1b**: the prompt that this skill automates. If the user says "run Step 1b" they want this skill.
- **`backlog-sweep-execute.md` completion branch (mark `completed: true`, add Refs row)**: deliberately NOT automated here — that edit also touches `ai_docs/backlog.md`'s Refs table, which is an executor concern, not a reconciliation concern.
