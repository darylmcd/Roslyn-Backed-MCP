---
name: release-cut
description: "Atomic release pipeline — bump -> verify -> ship -> tag -> reinstall-both-layers. Use when: cutting a new release (major/minor/patch), turning [Unreleased] CHANGELOG content into a tagged+published version, or after validating a batch of merges ready for v-bump. Takes bump type as input: 'major', 'minor', or 'patch'. Delegates to /bump, /ship, and /roslyn-mcp:update; checkpointed so mid-flow failure is re-runnable."
user-invocable: true
argument-hint: "patch | minor | major"
---

# Release Cut

You are a release engineer running the full release pipeline as a single atomic flow. Your job is to sequence the four discrete release phases (bump -> verify -> ship -> tag -> reinstall-both-layers) with shared error handling and checkpointed step output, so a mid-flow failure re-runs from the last successful checkpoint rather than restarting from the top.

This skill exists because the four phases were previously invoked one-by-one and kept hitting the same Layer-2 gap across sessions: the `dotnet publish -p:ReinstallTool=true` command refreshes the global tool binary (Layer 1) but leaves the plugin cache stale (Layer 2), so consumers loaded the new binary alongside old skill/hook metadata. Delegating to `/roslyn-mcp:update` (which wraps both layers) is the fix.

## Input

The `$ARGUMENTS` parameter is the bump type: `patch`, `minor`, or `major`. If not provided, ask the user which type of bump to perform with a brief explanation:
- **patch** (1.27.0 -> 1.27.1): bug fixes, doc changes, no new features
- **minor** (1.27.0 -> 1.28.0): new features, new stable tools, backward-compatible changes
- **major** (1.27.0 -> 2.0.0): breaking changes to stable tool contracts

## Refusal conditions (check these FIRST, before any action)

Refuse and report cleanly — do NOT produce a partial release — if any of the following hold:

- Working tree has uncommitted changes (`git status --porcelain` non-empty). Message: "Refusing: working tree must be clean before a release cut. Commit or stash first."
- Current branch is not `main`. Message: "Refusing: release cuts run from main. Current branch: <branch>."
- `main` is not up-to-date with `origin/main` (after `git fetch`). Message: "Refusing: local main is behind origin/main. Run git pull --ff-only first."
- `$ARGUMENTS` is not one of `patch` / `minor` / `major`. Message: "Refusing: bump type must be 'patch', 'minor', or 'major'. Got: <value>."

All four checks run BEFORE Step 1. Emit the refusal and stop — do not proceed to any delegated skill.

## Checkpoints

Each step records its result (stdout + exit code + any derived values like the new version string) so a re-invocation can detect completed work and skip forward. The implicit checkpoint tests per step:

- **Step 1 Preflight** — always re-run (cheap).
- **Step 2 Bump** — read `Directory.Build.props`; if `<Version>` already equals the computed new version AND `CHANGELOG.md` has a `## [X.Y.Z] - YYYY-MM-DD` header for that version, skip.
- **Step 3 Verify** — if `artifacts/verify-release.log` exists and its mtime is newer than the newest commit, skip. Otherwise re-run.
- **Step 4 Ship** — `gh pr list --state merged --head release/vX.Y.Z --limit 1`; if a merged PR exists for the bump branch, skip.
- **Step 5 Tag** — `git tag -l vX.Y.Z`; if the tag exists locally AND on origin (`git ls-remote --tags origin refs/tags/vX.Y.Z`), skip.
- **Step 6 Reinstall** — call `server_info`; if `version` matches the new version AND `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/` contains only the new-version directory, skip.

The probes are best-effort; when uncertain, re-run the step. Re-running `/bump` on an already-bumped repo is a no-op when all 5 files agree (verify-version-drift.ps1 short-circuits).

## Workflow

### Step 1: Preflight

Run these in parallel via Bash:

```
git status --porcelain
git rev-parse --abbrev-ref HEAD
git fetch origin
git rev-list --left-right --count origin/main...HEAD
```

If any refusal condition fires, emit the refusal and STOP.

Record the current version by reading `Directory.Build.props` (extract the `<Version>` value). Compute the new version from the bump type:
- `patch`: increment patch (1.27.0 -> 1.27.1)
- `minor`: increment minor, reset patch (1.27.0 -> 1.28.0)
- `major`: increment major, reset minor and patch (1.27.0 -> 2.0.0)

Display current -> new and the planned flow (Bump, Verify, Ship, Tag, Reinstall). No confirmation prompt — the user invoked the skill with an explicit bump type.

### Step 2: Bump (delegate to `/bump`)

Invoke the `/bump` skill with the same `$ARGUMENTS` value. That skill:
- Edits all 5 version files (`Directory.Build.props`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `manifest.json`, `CHANGELOG.md`).
- Prepends a `## [X.Y.Z] - YYYY-MM-DD` header to `CHANGELOG.md`.
- Runs `eng/verify-version-drift.ps1` to confirm all 5 files agree.

After `/bump` returns, verify:
- `eng/verify-version-drift.ps1` exit 0.
- `git diff --name-only` shows exactly the 5 expected paths.

If the CHANGELOG.md `## [Unreleased]` section had content, `/bump` moved it under the new version header. If the section was empty, remind the user: "CHANGELOG.md section is empty — fill in before Ship, or abort with git reset --hard origin/main." Pause for the user to review/edit before Step 3.

### Step 3: Verify

Run the full CI-equivalent locally:

```
pwsh -NoProfile -File eng/verify-release.ps1 -Configuration Release
```

Save stdout+stderr to `artifacts/verify-release.log` (tee pattern) so Step 4 Ship has evidence and re-runs can probe the log mtime. If exit code is non-zero: STOP and report the failure. Do NOT proceed to Ship with a red verify.

Also run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` (fast; covers shipped-skill generality + link check).

### Step 4: Ship (delegate to `/ship`)

**Contract assumed from `/ship` (user-global skill at `~/.claude/skills/ship/SKILL.md`, not version-pinned to this repo):**
- Creates a release branch (convention: `release/vX.Y.Z`) if the working tree has uncommitted version-file edits.
- Commits the 5 version-file edits with message `release: vX.Y.Z` (exact casing per precedent).
- Pushes the branch with `-u origin`.
- Opens a PR with title `release: vX.Y.Z`.
- Waits for checks to go green.
- Squash-merges with `--delete-branch`.
- Cleans up the local branch + any worktree created by ship itself.

If `/ship` is unavailable or its contract has drifted, fall back to the manual sequence (run from the primary repo root, not a worktree):

```
git checkout -b release/vX.Y.Z
git add -- \
  Directory.Build.props \
  .claude-plugin/plugin.json \
  .claude-plugin/marketplace.json \
  manifest.json \
  CHANGELOG.md
git commit -m "release: vX.Y.Z"
git push -u origin release/vX.Y.Z
gh pr create --title "release: vX.Y.Z" --body "Version bump to X.Y.Z. See CHANGELOG.md for details."
gh pr merge <n> --squash --delete-branch
git checkout main
git fetch origin && git reset --hard origin/main
```

After Ship completes: `git log -1 --format=%s` on main should show `release: vX.Y.Z (#<n>)`.

### Step 5: Tag

From the primary repo root (not a worktree), on `main` synced to `origin/main`:

```
git fetch origin
git checkout main
git reset --hard origin/main
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin vX.Y.Z
```

Verify the tag is on origin:

```
git ls-remote --tags origin refs/tags/vX.Y.Z
```

If the tag already exists on origin (prior release-cut attempt got this far), skip. If the local and remote SHAs disagree, STOP and report — do not force-push a tag.

### Step 6: Reinstall (delegate to `/roslyn-mcp:update`)

Invoke `/roslyn-mcp:update`. That skill handles both layers:

- **Layer 1 — Global tool binary.** Runs `dotnet tool update -g Darylmcd.RoslynMcp`. In a maintainer checkout, `just tool-update` pulls from NuGet.org; `just tool-install-local` installs from the local nupkg after `just pack`. The literal pin for in-repo publish paths is `-p:ReinstallTool=true` (dash form — the `/p:` form mangles on bash-on-Windows).
- **Layer 2 — Claude Code plugin.** Tells the user to run `/plugin marketplace update roslyn-mcp-marketplace` and `/plugin install roslyn-mcp@roslyn-mcp-marketplace` in the Claude Code chat input (client-side slash commands, not agent-executed).

After `/roslyn-mcp:update` returns, verify both layers:

1. **Layer 1:** Call `mcp__roslyn__server_info`. Confirm the reported `version` equals the new version (strip the `+hash` suffix before comparing).
2. **Layer 2:** Inspect `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/` — confirm only the new-version subdirectory is present. If stale subdirectories remain, they will be pruned on the next Claude Code restart; surface this to the user as a reminder.

If either verification fails: report the specific layer and STOP. Do not claim "release complete" without both layers green.

### Step 7: Report

Display a summary:

```
Release vX.Y.Z cut complete.

Step 1 Preflight   ok (clean tree, on main, up-to-date)
Step 2 Bump        ok (5 files -> X.Y.Z)
Step 3 Verify      ok (N tests, artifacts/verify-release.log)
Step 4 Ship        ok (PR #<n>, merged at <time>)
Step 5 Tag         ok (vX.Y.Z on origin)
Step 6 Reinstall   ok (Layer 1: server_info reports X.Y.Z; Layer 2: cache shows only X.Y.Z)

Reminder: restart Claude Code to load the updated binary, skills, and hooks.
Next: monitor the publish-nuget workflow triggered by the vX.Y.Z tag (if configured).
```

If any step is in a skipped state (re-run from mid-flow failure), annotate it `skipped (already done)` instead of `ok`.

## Example invocation

**Input:** `/release-cut patch`

**What you see:**

```
Current: 1.27.0
New:     1.27.1 (patch)
Plan:    Bump -> Verify -> Ship -> Tag -> Reinstall

Step 1 Preflight... ok (clean tree, on main, up-to-date with origin/main)
Step 2 Bump      ... ok (Directory.Build.props, plugin.json, marketplace.json, manifest.json, CHANGELOG.md -> 1.27.1)
                   [CHANGELOG.md ## [1.27.1] - 2026-04-22 header inserted. Review section content.]
Step 3 Verify    ... ok (N tests pass, artifacts/verify-release.log written)
Step 4 Ship      ... ok (PR #<n> opened, checks green, squash-merged)
Step 5 Tag       ... ok (v1.27.1 pushed to origin)
Step 6 Reinstall ... ok (server_info version 1.27.1; ~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/ shows only 1.27.1)

Release v1.27.1 cut complete.
Reminder: restart Claude Code to load the updated binary, skills, and hooks.
```

## Non-goals

- **Do NOT edit `ai_docs/backlog.md`** — backlog closures belong in the PR that ships the underlying change, not the release-cut PR.
- **Do NOT publish to NuGet directly** — the `v*` tag push triggers the `publish-nuget` workflow (or manual `eng/publish-nuget.ps1`). This skill stops after the tag.
- **Do NOT re-run `/bump` after Ship** — the bump is part of the release PR; re-running would create a second commit with a redundant version edit.
- **Do NOT merge your own PR in the Ship step if the primary repo root is a worktree** — `gh pr merge` fails inside a worktree (the branch is already used by the worktree). Run ship-related merges from `$(git rev-parse --git-common-dir)/..` (see `ai_docs/workflow.md` Worktree + gh pr merge discipline).

## Distinct from related skills

- **`/bump`**: version-file edits only. Does not verify, ship, tag, or reinstall. Invoked as Step 2 of this skill.
- **`/publish-preflight`**: checklist of validations (version drift, AI docs, build/test, CHANGELOG, security versions). Overlaps Step 3 but does NOT advance past verify. Run `/publish-preflight` ad-hoc to gate readiness; `/release-cut` assumes it has already passed (or runs the superset via `verify-release.ps1`).
- **`/ship`**: commit + push + PR + squash-merge. No version bump, no tag, no reinstall. Invoked as Step 4 of this skill.
- **`/roslyn-mcp:update`**: both-layer update only. No bump, no tag. Invoked as Step 6 of this skill.
