---
name: release-cut
installed_as: release-cut
description: "Atomic release pipeline — bump -> verify -> ship -> tag -> refresh-plugin. Use when cutting a major/minor/patch release. Delegates to /bump, /ship, and the maintainer-local /update; checkpointed so mid-flow failure is re-runnable."
user-invocable: true
argument-hint: "patch | minor | major"
---

# Release Cut

You are a release engineer running the full release pipeline as a single atomic flow. Sequence the release phases (bump -> verify -> ship -> tag -> refresh-plugin) with shared error handling and checkpointed step output, so a mid-flow failure re-runs from the last successful checkpoint rather than restarting from the top.

The plugin carries an exact-version `dnx` package pin together with its skills and hooks. Step 6 refreshes that single plugin contract through the maintainer-local `/update` skill; a global-tool install is an optional standalone-client canary, not a plugin dependency.

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
- `pwsh -NoProfile -File eng/verify-surface-snapshot-freshness.ps1` exits non-zero. Message: "Refusing: the .ai-doc-audit.md Live Surface snapshot has drifted from the live surface. Run /surface-audit, refresh the table, and re-date the heading first." The script prints the per-row delta; re-measure rather than hand-editing the numbers to green.

All five checks run BEFORE Step 2. Emit the refusal and stop — do not proceed to any delegated skill.

## Checkpoints

Each step records its result (stdout + exit code + any derived values like the new version string) so a re-invocation can detect completed work and skip forward. The implicit checkpoint tests per step:

- **Step 1 Preflight** — always re-run (cheap).
- **Step 2 Bump** — read `Directory.Build.props`; if `<Version>` already equals the computed new version AND `CHANGELOG.md` has a `## [X.Y.Z] - YYYY-MM-DD` header for that version, skip.
- **Step 3 Verify** — if `artifacts/verify-release.log` exists and its mtime is newer than the newest commit, skip. Otherwise re-run.
- **Step 4 Ship** — `gh pr list --state merged --head release/vX.Y.Z --limit 1`; if a merged PR exists for the bump branch, skip.
- **Step 5 Tag** — `git tag -l vX.Y.Z`; if the tag exists locally AND on origin (`git ls-remote --tags origin refs/tags/vX.Y.Z`), skip.
- **Step 6 Reinstall** — call `server_info`; if `version` matches the new version AND `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/` contains only the new-version directory, skip.

The probes are best-effort; when uncertain, re-run the step. Re-running `/bump` on an already-bumped repo is a no-op when all 7 files agree (verify-version-drift.ps1 short-circuits).

## Workflow

### Step 1: Preflight

Run the status and branch reads in parallel, then fetch before measuring divergence:

```
git status --porcelain
git rev-parse --abbrev-ref HEAD
git fetch origin
git rev-list --left-right --count origin/main...HEAD
pwsh -NoProfile -File eng/verify-surface-snapshot-freshness.ps1
```

The final command is ordered after `git fetch`; never use a pre-fetch remote-tracking ref as the
up-to-date proof.

The snapshot gate is the only non-git preflight check. It compares the `.ai-doc-audit.md` Live Surface table against the README live-surface paragraph (CI-guarded by `ReadmeSurfaceCountTests` against `ServerSurfaceCatalog`) and the shipped `skills/` directory, so it needs no Roslyn workspace load. It exists because that snapshot drifted across twelve tagged releases while its backlog row sat unpicked at Low — a prose reminder did not hold.

If any refusal condition fires, emit the refusal and STOP.

Record the current version by reading `Directory.Build.props` (extract the `<Version>` value). Compute the new version from the bump type:
- `patch`: increment patch (1.27.0 -> 1.27.1)
- `minor`: increment minor, reset patch (1.27.0 -> 1.28.0)
- `major`: increment major, reset minor and patch (1.27.0 -> 2.0.0)

Display current -> new and the planned flow (Bump, Verify, Ship, Tag, Reinstall). No confirmation prompt — the user invoked the skill with an explicit bump type.

### Step 2: Bump (delegate to `/bump`)

Invoke the `/bump` skill with the same `$ARGUMENTS` value. That skill:
- Edits all 7 version files (`Directory.Build.props`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `manifest.json`, `.claude-plugin/mcp.json`, `.claude-plugin/server.json`, `CHANGELOG.md`).
- Prepends a `## [X.Y.Z] - YYYY-MM-DD` header to `CHANGELOG.md`.
- Runs `eng/verify-version-drift.ps1` to confirm all 7 files agree.

After `/bump` returns, verify:
- `eng/verify-version-drift.ps1` exit 0.
- `git diff --name-only` shows the 7 version-file paths plus the consumed `changelog.d/*.md` fragment deletions.

If the CHANGELOG.md `## [Unreleased]` section had content, `/bump` moved it under the new version header. If the section was empty, remind the user: "CHANGELOG.md section is empty — fill in before Ship, or stop and explicitly revert the reviewed version-file edits." Pause for the user to review/edit before Step 3. Never discard a working tree with `git reset --hard`.

### Step 3: Verify

Run the cheap environment precheck before the publish gate:

```
pwsh -NoProfile -File eng/verify-release-environment.ps1
```

The check fails closed unless all of these are true:

| Signal | Required state |
|---|---|
| Available physical memory | At least 4 GiB and at least 20% of installed memory |
| Pre-existing `testhost` / `MSBuild` / `VBCSCompiler` processes | Zero |
| Pre-existing `dotnet` processes | At most 8 |

Exit `2` is an environment refusal, not a product failure. Close the owning build/test sessions,
run `dotnet build-server shutdown`, terminate only processes whose ownership you verified, and
re-run the precheck. Never use image-wide `taskkill` or `killall` commands. Exit `1` means the
machine state could not be inspected; stop rather than treating the publish gate as trustworthy.

Run the publish-gate invocation locally — the same entry point and flags
`publish-nuget.yml` runs at tag time:

```
pwsh -NoProfile -File eng/verify-release.ps1 -Configuration Release -NoCoverage -RequireConsumedFragments
```

**The flags are load-bearing — do NOT drop them to the bare form.**

| Flag | Why |
|---|---|
| `-NoCoverage` | Coverage is informational per `CI_POLICY.md` and gates nothing. The bare form is the dispatch/schedule coverage path, and it trips the known Coverlet .NET 10 teardown crash (`coverlet-net10-session-end-crash-upgrade`) — an `AccessViolationException` in `CoverletInProcDataCollector.TestSessionEnd` that aborts the run and returns exit 1 *after every test has already passed*. That is a false red on an otherwise green release. |
| `-RequireConsumedFragments` | Enforces that a consumed breaking section advances the major. Step 3 is the only local gate for the breaking→major rule; without this flag a mis-typed bump reaches Ship unchecked. |

Reference invocations (keep this table in sync with the workflows):

| Gate | Invocation |
|---|---|
| PR merge gate (`ci.yml`) | `-Configuration Release -NoCoverage -ExcludeNetworkTests` |
| Publish gate (`publish-nuget.yml`) | `-Configuration Release -NoCoverage -RequireConsumedFragments` |
| dispatch / schedule (informational only) | `-Configuration Release` |

Save stdout+stderr to `artifacts/verify-release.log` (tee pattern) so Step 4 Ship has evidence and re-runs can probe the log mtime. If exit code is non-zero: STOP and report the failure. Run `dotnet build-server shutdown` even after a killed or timed-out invocation, then re-run `eng/verify-release-environment.ps1` before retrying. Do NOT proceed to Ship with a red verify.

If the run reports `Failed: 0` yet still exits non-zero, check the tail for a collector crash before treating it as a product failure — see the `-NoCoverage` row above.

For every red verify, record the precheck snapshot and re-check current machine state before
classifying the result. Measured pressure is a separate signal, not proof that a product crash is
environmental; reproduce on a clean runner before closing a product investigation.

Also run `pwsh -NoProfile -File eng/verify-ai-docs.ps1` (fast; covers shipped-skill generality + link check).

### Step 4: Ship (delegate to `/ship`)

**Contract assumed from `/ship` (user-global skill at `~/.claude/skills/ship/SKILL.md`, not version-pinned to this repo):**
- Creates a release branch (convention: `release/vX.Y.Z`) if the working tree has uncommitted version-file edits.
- Commits the 7 version-file edits plus the consumed `changelog.d/*.md` fragment deletions with message `release: vX.Y.Z` (exact casing per precedent).
- Pushes the branch with `-u origin`.
- Opens a PR with title `release: vX.Y.Z`.
- Waits for checks to go green.
- Squash-merges with `--delete-branch`.
- Cleans up the local branch + any worktree created by ship itself.

**Worktree teardown discipline (Windows).** If this release cut runs from a worktree, and the release workspace was loaded into the Roslyn MCP server during the cut, call `workspace_close(workspaceId: <id>, drainProcesses: true)` BEFORE any `git worktree remove` call. The `drainProcesses: true` flag runs `dotnet build-server shutdown` after session disposal, releasing `VBCSCompiler.exe` / `MSBuild.exe` file-system locks that would otherwise cause `Permission denied` on `git worktree remove`. Omit this step only if you are certain no MCP workspace was loaded from the worktree path.

If `/ship` is unavailable or its contract has drifted, fall back to the manual sequence (run from the primary repo root, not a worktree):

```
git checkout -b release/vX.Y.Z
# /bump already produced exactly the change set to commit: the 7 version files
# (incl. .claude-plugin/mcp.json and .claude-plugin/server.json) modified + the consumed changelog.d/*.md
# fragment deletions. The tree was clean before the cut (refusal gate), so
# `git add -A` stages precisely those paths — no hand-maintained list to drift.
git add -A
git commit -m "release: vX.Y.Z"
git push -u origin release/vX.Y.Z
gh pr create --title "release: vX.Y.Z" --body "Version bump to X.Y.Z. See CHANGELOG.md for details."
gh pr merge <n> --squash --delete-branch
git checkout main
git fetch origin
git pull --ff-only
```

After Ship completes: `git log -1 --format=%s` on main should show `release: vX.Y.Z (#<n>)`.

### Step 5: Tag

From the primary repo root (not a worktree), on `main` synced to `origin/main`:

```
git fetch origin
git checkout main
git pull --ff-only
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin vX.Y.Z
```

Verify the tag is on origin:

```
git ls-remote --tags origin refs/tags/vX.Y.Z
```

If the tag already exists on origin (prior release-cut attempt got this far), skip. If the local and remote SHAs disagree, STOP and report — do not force-push a tag.

### Step 6: Reinstall (delegate to the maintainer-local `/update`, NOT the shipped `/roslyn-mcp:update`)

`release-cut` runs from inside the checkout, where `.claude/skills/update/SKILL.md` refreshes the plugin cache agent-executably via `eng/update-claude-plugin.ps1`. The plugin-namespaced shipped form can only instruct the user to run client-side `/plugin` commands. **Do not invoke the namespaced form here.**

Invoke the bare-name form: `Skill: update` (or `/update` interactively). It refreshes the marketplace clone and plugin cache, including the exact `Darylmcd.RoslynMcp@X.Y.Z` launch pin. Inspect the cache and confirm only the new-version directory remains. After NuGet publication/indexing completes, run the pinned `dnx` command from `.claude-plugin/mcp.json` as the server canary. A separate global-tool update is optional for maintainers who use Option A.

If either the cache check or pinned-package canary fails, STOP and report it; NuGet indexing may require a retry after a short delay.

### Step 7: Report

Display a summary:

```
Release vX.Y.Z cut complete.

Step 1 Preflight   ok (clean tree, on main, up-to-date)
Step 2 Bump        ok (7 files -> X.Y.Z)
Step 3 Verify      ok (N tests, artifacts/verify-release.log)
Step 4 Ship        ok (PR #<n>, merged at <time>)
Step 5 Tag         ok (vX.Y.Z on origin)
Step 6 Refresh     ok (plugin cache and pinned dnx server report X.Y.Z)

Reminder: restart Claude Code to load the updated server pin, skills, and hooks.
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
Step 2 Bump      ... ok (Directory.Build.props, plugin.json, marketplace.json, manifest.json, mcp.json, server.json, CHANGELOG.md -> 1.27.1)
                   [CHANGELOG.md ## [1.27.1] - 2026-04-22 header inserted. Review section content.]
Step 3 Verify    ... ok (N tests pass, artifacts/verify-release.log written)
Step 4 Ship      ... ok (PR #<n> opened, checks green, squash-merged)
Step 5 Tag       ... ok (v1.27.1 pushed to origin)
Step 6 Refresh   ... ok (pinned dnx server version 1.27.1; plugin cache shows only 1.27.1)

Release v1.27.1 cut complete.
Reminder: restart Claude Code to load the refreshed pinned server, skills, and hooks.
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
- **`/update` (maintainer-local override at `.claude/skills/update/`)**: agent-executable plugin cache refresh via `pwsh eng/update-claude-plugin.ps1`, plus an optional standalone global-tool update. **This is what Step 6 invokes.**
- **`/roslyn-mcp:update` (shipped, plugin-namespaced)**: documents the consumer-side `/plugin marketplace update` + `/plugin install` flow and optional global-tool path.
