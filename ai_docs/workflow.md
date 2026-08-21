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
- Change-bearing work must include a changed, valid `changelog.d/<row-id>.md`
  fragment. Internal `ai_docs/**`-only planning/provenance is exempt.
- The repo-local Codex `PreToolUse` hook runs the authoritative fragment
  verifier before commit, push, PR create/merge, and ship preflight. The hook is
  early feedback only; `verify-release.ps1` remains authoritative when hooks
  are disabled or untrusted.

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

See `~/.claude/prompts/backlog-sweep-execute.md` § Step 8 for the subagent-flow wording (global skill prompt; not in this repo).

## Release-managed file guard

A repo-local PreToolUse hook in `.claude/settings.json` (matcher
`Edit|Write|MultiEdit`) runs the canonical guard script and blocks edits to
release-managed files unless the agent explicitly acknowledges the policy. The
guarded set:

| # | Path | Why guarded |
|---|------|-------------|
| 1 | `Directory.Build.props` | Canonical `<Version>` source. |
| 2 | `BannedSymbols.txt` | Repo-wide banned-API list (release-critical analyzer input). |
| 3 | `manifest.json` (repo root) | Version mirror — one of 7 enumerated by `eng/verify-version-drift.ps1`. |
| 4 | `.claude-plugin/plugin.json` | Plugin manifest version. |
| 5 | `.claude-plugin/marketplace.json` | Marketplace manifest version (`plugins[0].version`). |
| 6 | `.claude-plugin/mcp.json` | Exact `Darylmcd.RoslynMcp@<version>` plugin launch pin. |
| 7 | `.claude-plugin/server.json` | MCP Registry manifest version (`version` and `packages[0].version`). |
| 8 | `CHANGELOG.md` (repo root) | Top `## [X.Y.Z]` header is part of the version-drift check. |
| 9 | `eng/verify-version-drift.ps1` | The drift-detector script itself. |
| 10 | `hooks/hooks.json` | The shipped hook config — release-critical even though the edit guard itself is repo-local. |
| 11 | `eng/verify-skills-are-generic.ps1` | The skills-genericity guard script. |

Files 1 and 3 through 8 are the seven version-source locations enumerated by
`eng/verify-version-drift.ps1`. Files 2 and 9 through 11 are additional
release-critical infrastructure. Treat `eng/guard-release-managed-files.ps1`
as the canonical path list; this table documents that executable contract.

**Bypass mechanism.** The guard is command-based — `eng/guard-release-managed-files.ps1`
inspects only `tool_input.file_path` (deterministic) and looks for an override
sentinel at the repo root: `.release-managed-edit-allowed` (gitignored). If the
sentinel exists and its mtime is within the TTL (default 1800 s, override via
`RELEASE_SENTINEL_TTL_SECONDS`), the edit is allowed. Otherwise the edit is
blocked with exit code 2 and a message pointing back here.

To bypass for an intentional ad-hoc edit, `touch` the sentinel:

    New-Item -ItemType File -Force .release-managed-edit-allowed | Out-Null

…then perform the edit. The sentinel is gitignored and stale-cleaned by the
TTL. Skills that mutate release-managed files (`/bump`, `/release-cut`,
`/ship`) create the sentinel before mutating and remove it at end of flow, so
the override is invisible to normal release workflows.

**History note.** The original guard (1.32.x and earlier) was prompt-based and
required the literal phrase `ack: release-managed` in the agent's reasoning
prose. In practice the judge LLM did not reliably receive that prose — it
either denied legitimate `/bump` flows or hallucinated rule matches against
unrelated new files. The sentinel-based replacement landed in 1.33.1 (see
CHANGELOG).

**Canonical workflows that don't need to touch the sentinel manually:**

- Bumping the version: use `/bump <major|minor|patch>` — the bump skill edits
  the seven version sources (files 1 and 3 through 8) atomically and manages the
  sentinel for you.
- Cutting a release: use `/release-cut` — wraps `/bump` end-to-end.
- Changelog fragments: write to `changelog.d/<row-id>.md` (NOT to
  `CHANGELOG.md` directly). `/bump` consumes the fragments at release time.

**False-positive note.** Test fixtures named `manifest.json` under
`tests/**/Fixtures/` are not the version source and are not blocked. The guard
script's path filter excludes `(^|/)(tests|fixtures)/`.

**Relationship to CI gate.** This hook is advisory-at-edit-time;
`eng/verify-version-drift.ps1` (run from `eng/verify-release.ps1`) remains the
authoritative merge gate. The hook prevents accidental drift earlier — at the
agent's keystroke rather than at PR review.

## Ownership

- Validation and merge gating belong to `../CI_POLICY.md`.
- Runtime constraints belong to `runtime.md`.
