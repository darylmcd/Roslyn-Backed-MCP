---
name: recover-stalled-subagent
description: "Recover a stalled initiative-executor subagent's uncommitted worktree into a WIP branch + remove the worktree cleanly in one invocation. Use when: an orchestrator spawn returned a malformed result (no `<<<RESULT>>>` sentinel) or emitted an internal-thought fragment as its final message, and the subagent's `.worktrees/<id>` still contains uncommitted work that must not be lost. Takes the remediation branch name as input. Alternative to the 3-turn manual `cd -> git status -> stage -> commit -> push -> cd back -> build-server shutdown -> worktree remove` dance."
user-invocable: true
argument-hint: "<branch-name> (e.g. remediation/subagent-malformed-result-orphans-uncommitted-work)"
---

# Recover Stalled Subagent

You are a recovery operator. Your job is to take a single stalled subagent's branch name, salvage any uncommitted work in its worktree into a WIP commit pushed to origin, then remove the worktree cleanly — leaving the orchestrator a 6-line STATE_HANDOFF block it can drop into state.json's `notes` field for the affected initiative.

This skill does **not** query GitHub, reconcile plan state, edit `state.json` / `plan.md` / `backlog.md` / `CHANGELOG.md`, or decide whether the initiative should be retried / deferred. Those are orchestrator decisions. This is a worktree-rescue primitive.

## Why this exists

Cold-subagent stalls (~1 in 10 spawn rate observed across 2026-04-17+18 passes) can emit a thought-fragment final message without the `<<<RESULT>>>` sentinel — see `ai_docs/prompts/backlog-sweep-execute.md` Appendix C's malformed-result detection. The orchestrator then cannot parse a PR URL, but the subagent may have already produced real edits in its worktree. 2026-04-18 pass-3 init-5 stranded 729 insertions across 4 prod files + 1 test file before a 3-turn manual recovery dance.

Manual recovery steps (each with its own foot-gun):

1. `cd .worktrees/<id>` and `git status --porcelain` — fine.
2. Stage files. If the operator reaches for `git add -A`, they pick up stray `.vs/` caches, `bin/`, `obj/`, or other ignored-but-dirty paths. Explicit per-file `git add -- <path>` is mandatory.
3. Commit with a well-formed WIP message citing the original branch.
4. Push to origin (so the WIP is durable before worktree removal).
5. `cd` back to primary repo root — NOT from inside the worktree, or the next step fails.
6. `dotnet build-server shutdown` **before** `git worktree remove --force`, or the Windows testhost/VBCSCompiler file lock on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/` leaves a stale `.worktrees/<id>` directory that the orchestrator must `rm -rf` manually (`Device or resource busy` on `git worktree remove`). This is the fix shipped by initiative 5 in PR #288; see `ai_docs/prompts/backlog-sweep-execute.md` Appendix A step 8 for the canonical discipline.
7. `git worktree remove --force .worktrees/<id>`.
8. Emit a STATE_HANDOFF block the orchestrator can paste into `state.json.initiatives[n].notes`.

The skill compresses steps 1-8 into one invocation that refuses cleanly on ambiguous state rather than partially-recovering.

## Input

`$ARGUMENTS` is a single token: the remediation branch name created by the stalled subagent. It may be given with or without the `remediation/` prefix:

- `/recover-stalled-subagent remediation/foo-bar`
- `/recover-stalled-subagent foo-bar`

Both resolve to branch `remediation/foo-bar` and worktree path `.worktrees/foo-bar`.

Required. If `$ARGUMENTS` is empty, print usage and exit without touching anything.

## Preconditions (HARD GATES — refuse if any fail)

Refuse cleanly and emit nothing else if any of these hold. Never partially-recover.

1. **Running inside a git repository.** `git rev-parse --git-common-dir` must succeed. If not: `"Refusing: not inside a git repository."`.
2. **Not already inside the target worktree.** The skill operates from the primary repo root. If the current directory resolves under `.worktrees/<id>` for the target id, refuse: `"Refusing: already inside .worktrees/<id>. cd back to the primary repo root and re-invoke."`.
3. **Worktree directory exists.** `.worktrees/<id>` must be a directory on disk. If not: `"Refusing: worktree .worktrees/<id> not found. Nothing to recover — the subagent may not have created a worktree, or it was already cleaned."`. This is not a bug — sometimes a subagent stalls before `git worktree add` — it just means there is no recovery to do.
4. **Branch matches worktree.** `git worktree list --porcelain` must show `.worktrees/<id>` bound to `refs/heads/remediation/<id>`. If the worktree is bound to a different branch (stale reuse, manual edit), refuse: `"Refusing: worktree .worktrees/<id> is bound to branch <other>, not remediation/<id>. Resolve manually."`.
5. **Branch is unpushed or ahead of origin.** The skill's purpose is to durably save LOCAL work. If `git log remediation/<id> --not origin/remediation/<id> --oneline` is empty AND `git status --porcelain` is also empty (checked in Step 1 below), the skill reports "nothing to recover" and skips to Step 6 (worktree removal). If local is ahead of origin (commits not yet pushed), the skill pushes them in Step 4 regardless of whether there is additional uncommitted work.

Preconditions 1-4 block all action. Precondition 5 just affects which branches of the workflow run.

## Workflow

### Step 1 — Inspect worktree state

From the primary repo root, run:

```bash
git -C .worktrees/<id> status --porcelain
git -C .worktrees/<id> log remediation/<id> --not origin/remediation/<id> --oneline 2>/dev/null || true
```

Classify the recovery situation:

| Uncommitted work (`status --porcelain`) | Unpushed commits | Action |
|---|---|---|
| Empty | Empty | **nothing-to-recover** — skip to Step 6 (worktree removal only). Emit a minimal STATE_HANDOFF. |
| Empty | Non-empty | **push-only** — skip Step 2-3 staging/commit. Run Step 4 push + Step 6 worktree removal. |
| Non-empty | Empty-or-not | **full-recovery** — run Step 2 staging, Step 3 WIP commit, Step 4 push, Step 6 worktree removal. |

Print the classification (one line) before proceeding so the user can see the decision.

### Step 2 — Stage explicit paths only

For **full-recovery** only.

Parse `git -C .worktrees/<id> status --porcelain`. Each line is `<XY> <path>` where `XY` is the two-column status code. For each path, issue an explicit `git -C .worktrees/<id> add -- <path>`. Handle deletions via `git -C .worktrees/<id> rm -- <path>` (for `D` / ` D`). Handle renames by adding both source and dest.

**Never use `git add -A` or `git add .`.** Ignored-but-dirty paths under `.vs/`, `bin/`, `obj/`, `.idea/` commonly appear in a stalled subagent's working tree; `-A` will pick them up and bloat the WIP commit with machine-specific cache data.

If the porcelain output is 200+ lines, print a one-line summary (count per status code) rather than dumping the full list; the full list is already in the git state.

### Step 3 — Create the WIP commit

For **full-recovery** only.

Derive a `<scope>` token from the branch name: strip the `remediation/` prefix, then take the first hyphen-delimited word as the scope. Examples:

| Branch | Scope |
|---|---|
| `remediation/workspace-health-triage-subagent` | `workspace` |
| `remediation/readme-surface-counts-drift-from-live-catalog` | `readme` |
| `remediation/di-lifetime-mismatch-detection` | `di` |
| `remediation/subagent-malformed-result-orphans-uncommitted-work` | `subagent` |

If the scope heuristic produces an obviously-wrong token (e.g. a non-word like `dr-9-12`, a numeric-only prefix), fall back to `wip` as the scope.

Commit with message:

```
wip(<scope>): <branch>-partial (subagent stalled)

Recovered by /recover-stalled-subagent after the initiative-executor subagent emitted a malformed or missing <<<RESULT>>> sentinel. This commit preserves the working-tree edits as they were at the stall. The orchestrator must decide whether to retry the initiative (rebasing or continuing from this tip), hand-finish on this branch, or abandon and defer.
```

Use a heredoc for the message so the second paragraph's line breaks survive. Do NOT add a `Co-Authored-By` trailer — this is a recovery commit, not an authored change.

### Step 4 — Push to origin

For **full-recovery** and **push-only**.

```bash
git -C .worktrees/<id> push -u origin remediation/<id>
```

If the push fails (auth expiry, network, existing branch on origin with divergent history), STOP and emit a failure STATE_HANDOFF — do NOT remove the worktree yet. The user must resolve the push manually before the worktree is safe to delete, or the recovered work is lost.

### Step 5 — cd back to primary repo root and run build-server shutdown

After Step 4 (or immediately from Step 1 for **nothing-to-recover**):

```bash
cd "$(git rev-parse --git-common-dir)/.."   # from anywhere; resolves to primary repo root
dotnet build-server shutdown
```

`dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` file locks left behind by `verify-release.ps1` (and any `dotnet test` a subagent ran mid-stall). On Windows this is the only reliable way to release the locks on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/` that block `git worktree remove --force`. The command is a no-op when no build-server is running, so it is always safe to prepend.

The command prints one informational line even in the no-op case (e.g. `"Build server didn't shut down due to an error or it wasn't running."`) — ignore this; it is not an error.

See `ai_docs/prompts/backlog-sweep-execute.md` Appendix A step 8 for the canonical shipped discipline (initiative 5 / PR #288).

### Step 6 — Remove the worktree

```bash
git worktree remove --force .worktrees/<id>
```

If this still fails with `Device or resource busy` after Step 5's `build-server shutdown`, STOP and emit a failure STATE_HANDOFF — do NOT fall back to `rm -rf .worktrees/<id>` without the git metadata update, or the worktree will appear stale in `git worktree list`. The user must identify the remaining lock-holder (look for `dotnet.exe` / `testhost.exe` processes via `tasklist | grep -i dotnet` on Windows) and terminate it before re-running the skill.

Do NOT delete the branch `remediation/<id>`. The orchestrator's retry or defer decision is made from the full plan-state view; the branch (now carrying the WIP tip on origin) is the durable record the orchestrator needs.

### Step 7 — Emit STATE_HANDOFF

Print exactly 6 lines, prefixed `STATE_HANDOFF:`, for the orchestrator to paste into `state.json.initiatives[n].notes`. The 6-line shape is load-bearing — the orchestrator's Appendix C parse expects it.

```
STATE_HANDOFF:
recovery: <full-recovery | push-only | nothing-to-recover | failed>
branch: remediation/<id>
wip-commit: <sha or "none">
origin-status: <pushed | not-pushed>
worktree: <removed | stale-blocked>
```

- `recovery` — which branch of the workflow ran.
- `branch` — always the full `remediation/<id>`, even when `$ARGUMENTS` omitted the prefix.
- `wip-commit` — SHA of the commit created in Step 3, or `none` for push-only / nothing-to-recover.
- `origin-status` — `pushed` if Step 4 succeeded or was skipped because origin was already up-to-date, `not-pushed` on Step 4 failure.
- `worktree` — `removed` if Step 6 succeeded, `stale-blocked` on failure (Step 5 + 6 exhausted without success).

After the STATE_HANDOFF block, emit a one-line human summary for the console (e.g. `"Recovered 729 insertions across 5 files -> pushed as <sha> -> worktree removed cleanly."`).

## Non-goals

- **Do NOT edit `state.json`, `plan.md`, `backlog.md`, `CHANGELOG.md`.** The orchestrator drops the STATE_HANDOFF block into state.json's notes; this skill does not touch those files.
- **Do NOT delete the `remediation/<id>` branch** (local or remote). The branch tip is the durable record the orchestrator uses for retry/defer/hand-finish.
- **Do NOT merge anything.** There is no PR to open — this is pre-PR recovery.
- **Do NOT decide retry vs defer.** That is an orchestrator judgement based on plan state + the recovered WIP diff; the skill has neither.
- **Do NOT run `verify-release.ps1` or any test.** The WIP commit is known-partial; validating it defeats the purpose (the orchestrator expects to rebuild from this tip, not inherit its pass/fail status).

## Refusal cases (explicit)

- **Empty `$ARGUMENTS`** → print usage and exit without touching anything.
- **Not in a git repo** → Precondition 1 refusal.
- **Invoked from inside the target worktree** → Precondition 2 refusal.
- **Worktree directory does not exist** → Precondition 3 refusal (reports "nothing to recover", exits 0 — not a failure mode).
- **Worktree bound to wrong branch** → Precondition 4 refusal with the actual bound branch.
- **Push fails in Step 4** → stop; emit `STATE_HANDOFF:` with `origin-status: not-pushed` and `worktree: stale-blocked`; do NOT remove the worktree.
- **Worktree removal fails after `build-server shutdown`** → stop; emit `STATE_HANDOFF:` with `worktree: stale-blocked` so the orchestrator can surface it to the human operator in the Step 10 report.

## Example

**Input:** `/recover-stalled-subagent remediation/workspace-health-triage-subagent`

```
Classification: full-recovery (4 modified files, 0 unpushed commits).
Staging explicit paths:
  A  .claude/agents/workspace-health-triage.md
  M  ai_docs/prompts/backlog-sweep-execute.md
  M  ai_docs/workflow.md
  M  README.md
Committing as wip(workspace): remediation/workspace-health-triage-subagent-partial (subagent stalled)... 1f3a9c2
Pushing to origin... ok
cd back to primary repo root...
Running dotnet build-server shutdown... (Build server didn't shut down due to an error or it wasn't running.)
Removing worktree .worktrees/workspace-health-triage-subagent... ok

STATE_HANDOFF:
recovery: full-recovery
branch: remediation/workspace-health-triage-subagent
wip-commit: 1f3a9c2
origin-status: pushed
worktree: removed

Recovered 4 modified files (623 insertions, 41 deletions) -> pushed as 1f3a9c2 -> worktree removed cleanly.
```

## Distinct from related skills

- **`/reconcile-backlog-sweep-plan`** — reconciles `state.json` + `plan.md` against `gh pr view` reality. Runs AFTER recovery, not before. Use this skill first to rescue the worktree and save the WIP to origin; then run `/reconcile-backlog-sweep-plan` to update plan-state with the retry/defer decision (including the STATE_HANDOFF block in `notes`).
- **`/close-backlog-rows`** — deletes rows from `ai_docs/backlog.md` atomically. Unrelated — this skill does not touch backlog.md. A stalled initiative whose row is already closed in backlog.md is still recoverable; a stalled initiative whose row is still open is also recoverable. The row's status and the worktree's recoverability are orthogonal.
- **`/ship`** — commits+pushes+opens PR+merges. Runs ONLY when a subagent completed cleanly (it produced a `<<<RESULT>>>` with `success:` + a PR URL). A stalled subagent is the opposite case — no PR, no merge; recover via this skill instead.
