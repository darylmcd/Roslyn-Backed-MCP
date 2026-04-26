# Workflow

<!-- purpose: Git branches, worktrees, PRs, and merge-ready handoff for this repo. -->

This document is the single owner for branch, worktree, pull-request, and merge-ready handoff workflow.

## Branching

- Use a task branch for write work.
- Keep one branch per concern.

## Concurrent Write Isolation

- If another write-capable session is active or likely, use a dedicated git worktree.
- Do not run concurrent write sessions against the same branch.

## Pull Requests

- Use a pull request for merge-ready handoff.
- Keep the pull request scoped to one concern.

## Merge-Ready Handoff

- Run the required validation from `../CI_POLICY.md`.
- Resolve merge conflicts before merge handoff.
- If repository settings require the branch to be up to date with base, sync it before merge.

## Backlog closure

- When a change **closes** rows in `ai_docs/backlog.md`, update that file in the **same PR** (or an immediate follow-up).
- **Implementation plans** must include a final todo such as `backlog: sync ai_docs/backlog.md` so the backlog stays aligned with shipped work.
- **Multi-repo deep-review campaigns:** run the `/backlog-intake` skill from repo root — it stages sibling audits via `eng/stage-review-inbox.ps1`, extracts/dedupes/anchor-verifies/splits, and merges rows into `ai_docs/backlog.md`. See `ai_docs/procedures/deep-review-backlog-intake.md`.

## Post-Merge Cleanup

- Delete merged task branches.
- Remove temporary worktrees created for the task.
- Start follow-up work on a new branch.

### Worktree `gh pr merge` discipline

`gh pr merge` fails inside a worktree with `fatal: '<branch>' is already used by
worktree at <primary-path>` because `gh` tries to update / delete a local
branch checked out in the primary repo. Run merge commands from the primary
repo root:

    cd "$(git rev-parse --git-common-dir)/.." && gh pr merge <n> --squash --delete-branch

See `prompts/backlog-sweep-execute.md` § Step 8 for the subagent-flow wording.

## Release-managed file guard

A PreToolUse hook in `hooks/hooks.json` (matcher `Edit|Write|MultiEdit`) blocks
edits to release-managed files unless the agent explicitly acknowledges the
policy. The guarded set:

| # | Path | Why guarded |
|---|------|-------------|
| 1 | `Directory.Build.props` | Canonical `<Version>` source. |
| 2 | `BannedSymbols.txt` | Repo-wide banned-API list (release-critical analyzer input). |
| 3 | `manifest.json` (repo root) | Version mirror — one of 5 enumerated by `eng/verify-version-drift.ps1`. |
| 4 | `.claude-plugin/plugin.json` | Plugin manifest version. |
| 5 | `.claude-plugin/marketplace.json` | Marketplace manifest version (`plugins[0].version`). |
| 6 | `CHANGELOG.md` (repo root) | Top `## [X.Y.Z]` header is part of the version-drift check. |
| 7 | `eng/verify-version-drift.ps1` | The drift-detector script itself. |
| 8 | `hooks/hooks.json` | The hook config — editing it accidentally would silently disarm guards. |
| 9 | `eng/verify-skills-are-generic.ps1` | The skills-genericity guard script. |

Files 1, 3, 4, 5, and 6 are the five version-source locations enumerated by
`eng/verify-version-drift.ps1`. Files 2, 7, 8, and 9 are additional
release-critical infrastructure.

**Bypass mechanism.** The guard is prompt-based (the LLM evaluator inspects the
recent assistant reasoning). To bypass it for an intentional edit, include the
literal phrase `ack: release-managed` (case-sensitive, with the colon and
space) in the assistant reasoning text accompanying the tool call. The
phrase must appear in natural-language reasoning — synthesizing it inside the
file `content` / `new_string` does not count.

**Canonical workflows that don't need the override:**

- Bumping the version: use `/bump <major|minor|patch>` — the bump skill edits
  files 1, 3, 4, 5, 6 atomically. Include `ack: release-managed` in the bump
  rationale to satisfy the guard.
- Cutting a release: use `/release-cut` — wraps `/bump` and includes the
  necessary acknowledgement.
- Changelog fragments: write to `changelog.d/<row-id>.md` (NOT to file 6
  directly). `/bump` consumes the fragments at release time.

**False-positive note.** Test fixtures named `manifest.json` under
`tests/**/Fixtures/` are not the version source and are not blocked. The
evaluator distinguishes by path context.

**Relationship to CI gate.** This hook is advisory-at-edit-time;
`eng/verify-version-drift.ps1` (run from `eng/verify-release.ps1`) remains the
authoritative merge gate. The hook prevents accidental drift earlier — at the
agent's keystroke rather than at PR review.

## Ownership

- Validation and merge gating belong to `../CI_POLICY.md`.
- Runtime constraints belong to `runtime.md`.
