---
name: recover-stalled-subagent
installed_as: recover-stalled-subagent
description: "Recover a stalled subagent's uncommitted worktree into a WIP branch and remove the worktree cleanly in one invocation. Use when: a subagent returned a malformed or empty final result and its `.worktrees/<id>` still contains uncommitted work that must not be lost. Takes the remediation branch name (with or without the `remediation/` prefix); stages explicit paths, commits a WIP commit, pushes it, runs `dotnet build-server shutdown`, removes the worktree, and prints a 6-line RECOVERY_SUMMARY block."
user-invocable: true
argument-hint: "<branch-name> (e.g. remediation/subagent-malformed-result-orphans-uncommitted-work)"
---

# Recover Stalled Subagent

You are a recovery operator. Take one stalled subagent's branch name, salvage any uncommitted work in its worktree into a WIP commit pushed to origin, then remove the worktree cleanly. Finish by printing a 6-line RECOVERY_SUMMARY block the caller can act on.

This skill does **not** query GitHub, edit `ai_docs/backlog.md` or `CHANGELOG.md`, or decide whether the work should be retried, deferred, or finished by hand. Those are caller decisions. This is a worktree-rescue primitive.

## Why this exists

A subagent can stall and return a thought fragment or nothing, with no PR URL, even though it already produced real edits in its worktree. Manual recovery has several foot-guns:

1. `git add -A` picks up ignored-but-dirty `.vs/`, `bin/`, `obj/` paths, so staging must be per-file.
2. The WIP commit must be pushed to origin before the worktree is removed, or the work is lost.
3. The worktree must be removed from the primary repo root, not from inside it.
4. `dotnet build-server shutdown` must run **before** `git worktree remove --force`. Otherwise a Windows testhost or VBCSCompiler file lock on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/` leaves a stale `.worktrees/<id>` directory (`Device or resource busy`).

This skill compresses the recovery into one invocation that refuses cleanly on ambiguous state rather than partially recovering.

## Input

`$ARGUMENTS` is a single token: the remediation branch name created by the stalled subagent, with or without the `remediation/` prefix:

- `/recover-stalled-subagent remediation/foo-bar`
- `/recover-stalled-subagent foo-bar`

Both resolve to branch `remediation/foo-bar` and worktree path `.worktrees/foo-bar`.

Required. If `$ARGUMENTS` is empty, print usage and exit without touching anything.

## Preconditions (refuse if any fail)

Refuse cleanly and emit nothing else if any of these hold. Never partially recover.

1. **Running inside a git repository.** `git rev-parse --git-common-dir` must succeed. If not: `"Refusing: not inside a git repository."`.
2. **Not already inside the target worktree.** The skill operates from the primary repo root. If the current directory resolves under `.worktrees/<id>` for the target id, refuse: `"Refusing: already inside .worktrees/<id>. cd back to the primary repo root and re-invoke."`.
3. **Worktree directory exists.** `.worktrees/<id>` must be a directory on disk. If not: `"Refusing: worktree .worktrees/<id> not found. Nothing to recover — the subagent may not have created a worktree, or it was already cleaned."`. Not a failure: a subagent can stall before `git worktree add`.
4. **Branch matches worktree.** `git worktree list --porcelain` must show `.worktrees/<id>` bound to `refs/heads/remediation/<id>`. Otherwise refuse: `"Refusing: worktree .worktrees/<id> is bound to branch <other>, not remediation/<id>. Resolve manually."`.

Preconditions 1-4 block all action. Whether the branch has unpushed or uncommitted work only selects the workflow path in Step 1.

## Workflow

### Step 1 — Inspect worktree state

From the primary repo root, run:

```bash
git -C .worktrees/<id> status --porcelain
git -C .worktrees/<id> log remediation/<id> --not origin/remediation/<id> --oneline 2>/dev/null || true
```

Classify and print the classification (one line) before proceeding:

| Uncommitted work (`status --porcelain`) | Unpushed commits | Action |
|---|---|---|
| Empty | Empty | **nothing-to-recover** — skip to Step 5 (build-server shutdown) then Step 6 (worktree removal). |
| Empty | Non-empty | **push-only** — skip Steps 2-3. Run Step 4 push, Step 5, Step 6. |
| Non-empty | Empty-or-not | **full-recovery** — run Steps 2-6. |

### Step 2 — Stage explicit paths only

For **full-recovery** only.

Parse `git -C .worktrees/<id> status --porcelain`. Each line is `<XY> <path>`. Issue an explicit `git -C .worktrees/<id> add -- <path>` per path. Handle deletions with `git -C .worktrees/<id> rm -- <path>` (for `D` / ` D`) and renames by adding both source and destination.

Never use `git add -A` or `git add .`. If the porcelain output is 200+ lines, print a one-line count per status code instead of the full list.

### Step 3 — Create the WIP commit

For **full-recovery** only.

Derive a `<scope>` token from the branch name: strip the `remediation/` prefix, then take the first hyphen-delimited word. If that yields an obviously wrong token (non-word, numeric-only prefix), use `wip`.

Commit with this message, passed via heredoc so the paragraph break survives:

```
wip(<scope>): <branch>-partial (subagent stalled)

Recovered by /recover-stalled-subagent after the subagent returned a malformed or missing result. This commit preserves the working-tree edits as they were at the stall. The caller decides whether to retry (rebasing or continuing from this tip), hand-finish on this branch, or abandon and defer.
```

Add no `Co-Authored-By` trailer: this is a recovery commit, not an authored change.

### Step 4 — Push to origin

For **full-recovery** and **push-only**.

```bash
git -C .worktrees/<id> push -u origin remediation/<id>
```

If the push fails (auth expiry, network, divergent history on origin), STOP and emit a failure RECOVERY_SUMMARY. Do not remove the worktree: the user must resolve the push first, or the recovered work is lost.

### Step 5 — Return to the primary repo root and shut down build servers

```bash
cd "$(git rev-parse --git-common-dir)/.."   # resolves to the primary repo root from anywhere
dotnet build-server shutdown
```

This releases the `testhost.exe` / `VBCSCompiler.exe` locks left by any `dotnet test` or `verify-release.ps1` the subagent ran mid-stall. It is a no-op when no build server runs, and its informational line ("Build server didn't shut down due to an error or it wasn't running.") is not an error.

### Step 6 — Remove the worktree

```bash
git worktree remove --force .worktrees/<id>
```

If this still fails with `Device or resource busy`, STOP and emit a failure RECOVERY_SUMMARY. Do not fall back to `rm -rf .worktrees/<id>`: the worktree would then appear stale in `git worktree list`. The user must find the remaining lock-holder (`tasklist | grep -i dotnet` on Windows), terminate it, and re-run the skill.

Do not delete the branch `remediation/<id>`; its tip on origin is the durable record of the recovered work.

### Step 7 — Emit RECOVERY_SUMMARY

Print exactly 6 lines, prefixed `RECOVERY_SUMMARY:`. The block is for the caller to read; nothing parses it.

```
RECOVERY_SUMMARY:
recovery: <full-recovery | push-only | nothing-to-recover | failed>
branch: remediation/<id>
wip-commit: <sha or "none">
origin-status: <pushed | not-pushed>
worktree: <removed | stale-blocked>
```

- `recovery` — which path of the workflow ran.
- `branch` — always the full `remediation/<id>`, even when `$ARGUMENTS` omitted the prefix.
- `wip-commit` — SHA of the Step 3 commit, or `none` for push-only / nothing-to-recover.
- `origin-status` — `pushed` if Step 4 succeeded or origin was already up to date, `not-pushed` on push failure.
- `worktree` — `removed` if Step 6 succeeded, `stale-blocked` otherwise.

After the block, print a one-line human summary (e.g. `"Recovered 4 modified files -> pushed as <sha> -> worktree removed cleanly."`).

## Non-goals

- Do not edit `ai_docs/backlog.md` or `CHANGELOG.md`.
- Do not delete the `remediation/<id>` branch, local or remote.
- Do not merge anything or open a PR; this is pre-PR recovery.
- Do not decide retry vs defer; that depends on context this skill does not have.
- Do not run `verify-release.ps1` or any test. The WIP commit is known-partial, and validating it defeats the purpose.

## Refusal and failure cases

- **Empty `$ARGUMENTS`** → print usage and exit without touching anything.
- **Not in a git repo, or invoked from inside the target worktree** → Precondition 1 / 2 refusal.
- **Worktree directory missing** → Precondition 3 refusal ("nothing to recover", exits 0).
- **Worktree bound to the wrong branch** → Precondition 4 refusal naming the actual bound branch.
- **Push fails in Step 4** → stop; emit `RECOVERY_SUMMARY:` with `origin-status: not-pushed` and `worktree: stale-blocked`; leave the worktree.
- **Worktree removal fails after `build-server shutdown`** → stop; emit `RECOVERY_SUMMARY:` with `worktree: stale-blocked`.

## Example

**Input:** `/recover-stalled-subagent remediation/workspace-health-triage-subagent`

```
Classification: full-recovery (4 modified files, 0 unpushed commits).
Staging explicit paths:
  A  .claude/agents/workspace-health-triage.md
  M  ai_docs/workflow.md
  M  README.md
Committing as wip(workspace): remediation/workspace-health-triage-subagent-partial (subagent stalled)... 1f3a9c2
Pushing to origin... ok
cd back to primary repo root...
Running dotnet build-server shutdown... (Build server didn't shut down due to an error or it wasn't running.)
Removing worktree .worktrees/workspace-health-triage-subagent... ok

RECOVERY_SUMMARY:
recovery: full-recovery
branch: remediation/workspace-health-triage-subagent
wip-commit: 1f3a9c2
origin-status: pushed
worktree: removed

Recovered 3 modified files -> pushed as 1f3a9c2 -> worktree removed cleanly.
```

## Distinct from related skills

- **`/ship`** — commits, pushes, opens a PR and merges. It applies only when work completed cleanly. A stalled subagent has no PR to land; recover its worktree with this skill instead.
