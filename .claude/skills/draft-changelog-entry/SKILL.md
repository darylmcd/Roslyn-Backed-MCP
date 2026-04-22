---
name: draft-changelog-entry
description: "Drafts one changelog fragment file under `changelog.d/<row-id>.md` from PR/commit metadata. Use when: preparing a PR for ship, writing a CHANGELOG entry for a merged or about-to-merge change, or filling `changelog.d/` from an initiative in a backlog-sweep `state.json`. Takes initiative id + commit SHA (or branch name) + backlog row id(s) closed. Non-destructive — emits the fragment file contents for review, never auto-commits, never touches `CHANGELOG.md` directly."
user-invocable: true
argument-hint: "<initiative-id> <commit-sha-or-branch> <row-id-1>[,<row-id-2>,...]"
---

# Draft Changelog Entry

You are a release-notes drafter. Your job is to write exactly ONE changelog fragment file at `changelog.d/<row-id>.md` from (a) the initiative's `changelogCategory` in the active backlog-sweep `state.json`, (b) the commit-message body on the named SHA/branch, and (c) the backlog row id(s) the change closes. You do NOT edit `CHANGELOG.md` — that file is consumed at release-cut time by `/bump`, which groups the fragments by category into the new `## [X.Y.Z]` section and `git rm`s them in the same commit. You do NOT commit, push, or touch git in any way — the user reviews the fragment file and commits themselves (typically via `/ship`).

See `changelog.d/README.md` for the fragment-file pattern rationale (parallel-PR merge-conflict avoidance) and the exact on-disk shape.

## Inputs

`$ARGUMENTS` must contain three space-separated tokens:

1. **Initiative id** — the `id` field in the active backlog-sweep `state.json`'s `initiatives[]` array (e.g. `mcp-connection-session-resilience`, `parallel-pr-changelog-append-friction`). Used to look up `changelogCategory`.
2. **Commit SHA or branch name** — source-of-truth for the prose. The skill reads the commit message body (not subject line) via `gh` or `git` and adapts it into the bullet's body. Examples: `remediation/parallel-pr-changelog-append-friction`, `abc1234`.
3. **Comma-separated backlog row id(s) closed** — e.g. `parallel-pr-changelog-append-friction` or `dr-9-1,dr-9-2` when an initiative bundles rows. Appears as the bold closing parenthesis at the end of the bullet. The FIRST id in the list also becomes the fragment filename.

If any of the three are missing, ask the user to supply them with a one-line example.

## Refusal conditions (check these FIRST, before any file edits)

Refuse and report cleanly — do NOT produce a partial fragment — if any of the following hold:

| Condition | Message |
|---|---|
| Active `state.json` cannot be located OR `schemaVersion != 2` | `"Refusing: unsupported backlog-sweep state schema. This skill requires schemaVersion == 2. Found: <value> at <path>."` |
| `gh` CLI is not on PATH (`command -v gh` fails) | `"Refusing: the 'gh' CLI is required to read commit metadata but was not found on PATH."` |
| A fragment file `changelog.d/<first-row-id>.md` already exists | `"Refusing: a fragment for row '<id>' already exists at changelog.d/<id>.md. This skill never clobbers existing fragments — edit the existing file by hand if it needs an update, or remove it first."` |
| The initiative id is not found in `state.json` OR its `changelogCategory` is null | `"Refusing: initiative '<id>' not found in <state.json path>, or its changelogCategory is null. Backfill changelogCategory in state.json first."` |
| The commit cannot be resolved (`git show <ref>` returns non-zero) | `"Refusing: cannot resolve commit / branch '<ref>'."` |
| `changelog.d/` directory does not exist at the repo root | `"Refusing: changelog.d/ directory not found at repo root. See changelog.d/README.md for the expected layout; ensure the parallel-pr-changelog-append-friction initiative has shipped."` |

Refusals must be emitted BEFORE any file is touched. Never emit a partial fragment.

## Workflow

### Step 1: Locate the active state.json

Find the most recent `ai_docs/plans/<timestamp>_backlog-sweep/state.json`:

```bash
ls -t ai_docs/plans/*_backlog-sweep/state.json | head -1
```

Read it. Confirm `schemaVersion == 2`. Locate the initiative whose `id` matches the first argument. Capture:

- `changelogCategory` — one of `"Fixed"`, `"Added"`, `"Changed"`, `"Changed — BREAKING"`, `"Maintenance"`. Must exactly match one of the five values in `changelog.d/README.md` (including the em-dash in `Changed — BREAKING`).

### Step 2: Read the commit body

Via `gh` (preferred, pulls trailers and merged-PR context) or `git` fallback:

```bash
git show -s --format=%B <commit-sha-or-branch>
```

The **subject line** is the commit title (e.g. `feat(skills): changelog.d fragment-file pattern (parallel-pr-changelog-append-friction)`). The **body** starts after the first blank line and carries the explanatory prose + row-id citations. Adapt the body into 1-3 sentences that mirror the format of existing shipped bullets under `CHANGELOG.md`'s most recent released `## [X.Y.Z]` section. Keep proper-nouns, tool names, and file paths verbatim; compress rationale prose.

### Step 3: Check for fragment collision

Before emitting, confirm that `changelog.d/<first-row-id>.md` does not already exist. If it does, refuse per the table above. (Don't scan `CHANGELOG.md` for the row id — `CHANGELOG.md` is source-of-truth for released versions only; the fragment directory is the only write-target for `[Unreleased]`-equivalent content.)

### Step 4: Draft the fragment contents

Format (see `changelog.d/README.md` for the reference example):

```
---
category: <Category>
---

- **<Category>:** <one-sentence summary of the change> (`<row-id-1>`[, `<row-id-2>`, ...]).
```

- `<Category>` in the frontmatter value AND the bold prefix must match exactly: `Fixed`, `Changed`, `Changed — BREAKING`, `Added`, or `Maintenance` (em-dash is U+2014, not two hyphens).
- When the commit body has multiple paragraphs of rationale (common for non-trivial fixes), include a second sentence only if it carries load-bearing context (root cause, observable symptom, regression-test name). Do NOT dump the whole commit body — the CHANGELOG is a summary, not an archive. The commit history is the archive.

### Step 5: Write the fragment file

Write the drafted contents to `changelog.d/<first-row-id>.md`. This is the ONLY file the skill creates or edits. Do NOT touch `CHANGELOG.md`. Do NOT run `git add`, `git commit`, or any other mutating git command.

If the filename collides with an unrelated existing fragment (rare — bundled-work case), disambiguate by reading `changelog.d/` first and appending `-2`, `-3` suffixes; cite the collision in the final report.

### Step 6: Emit the fragment contents and stop

Print the new fragment file contents to the transcript for the user's review. Report:

- Fragment path: `changelog.d/<first-row-id>.md`
- Category captured: `<Category>` (matches one of the five valid frontmatter values).
- Next step: `"Review the fragment. When ready, run /ship (or git add + git commit) to ship. The fragment will be consumed and deleted at the next /bump."`

## Example output

**Input:** `/draft-changelog-entry parallel-pr-changelog-append-friction remediation/parallel-pr-changelog-append-friction parallel-pr-changelog-append-friction`

**Lookup from `state.json`:** `changelogCategory = "Changed"`.

**Commit body (abbreviated):**
> Add `changelog.d/` fragment-file directory + update `draft-changelog-entry` and `bump` skills so every PR writes `changelog.d/<row-id>.md` instead of editing `CHANGELOG.md` under `## [Unreleased]`. Eliminates the merge-conflict hotspot that bit 50% of parallel subagents in 2026-04-17 pass-1. Release-cut (`/bump`) consumes fragments and groups them into the new `## [X.Y.Z]` section, then `git rm`s them in the same commit.

**Written file — `changelog.d/parallel-pr-changelog-append-friction.md`:**

```
---
category: Changed
---

- **Changed:** Changelog workflow moves to a `changelog.d/` fragment-file pattern (towncrier-style) — every PR writes `changelog.d/<row-id>.md` instead of appending to `CHANGELOG.md`, eliminating the merge-conflict hotspot that bit 50% of parallel subagents in 2026-04-17 pass-1. Release-cut consumes and groups fragments into `## [X.Y.Z]` at bump time. `draft-changelog-entry` and `bump` skills updated accordingly (`parallel-pr-changelog-append-friction`).
```

**Report:**
> Wrote `changelog.d/parallel-pr-changelog-append-friction.md` with category `Changed`. Review the fragment. When ready, run /ship to commit + push. Fragment will be consumed + deleted at the next /bump release-cut.
