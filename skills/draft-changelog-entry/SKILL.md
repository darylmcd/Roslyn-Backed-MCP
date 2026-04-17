---
name: draft-changelog-entry
description: "Drafts one CHANGELOG.md bullet from PR/commit metadata and atomically updates Maintenance tallies. Use when: preparing a PR for ship, writing a CHANGELOG entry for a merged or about-to-merge change, or filling `## [Unreleased]` subsections from an initiative in a backlog-sweep `state.json`. Takes initiative id + commit SHA (or branch name) + backlog row id(s) closed. Non-destructive — emits a diff for review, never auto-commits."
user-invocable: true
argument-hint: "<initiative-id> <commit-sha-or-branch> <row-id-1>[,<row-id-2>,...]"
---

# Draft Changelog Entry

You are a release-notes drafter. Your job is to emit exactly ONE CHANGELOG.md bullet under `## [Unreleased]` from (a) the initiative's `changelogCategory` in the active backlog-sweep `state.json`, (b) the commit-message body on the named SHA/branch, and (c) the backlog row id(s) the change closes. If the initiative's `### Maintenance` contributions are observable (tests added, rows closed), you additionally update the Maintenance subsection tallies atomically with the bullet insertion. You do NOT commit, push, or touch git in any way — the user reviews the diff and commits themselves (typically via `/ship`).

## Inputs

`$ARGUMENTS` must contain three space-separated tokens:

1. **Initiative id** — the `id` field in the active backlog-sweep `state.json`'s `initiatives[]` array (e.g. `mcp-connection-session-resilience`, `changelog-entry-draft-from-pr-metadata`). Used to look up `changelogCategory`, `testFilesAdded`, and `rowsClosedCount`.
2. **Commit SHA or branch name** — source-of-truth for the prose. The skill reads the commit message body (not subject line) via `gh` or `git` and adapts it into the bullet's body. Examples: `remediation/changelog-entry-draft-from-pr-metadata`, `abc1234`.
3. **Comma-separated backlog row id(s) closed** — e.g. `changelog-entry-draft-from-pr-metadata` or `dr-9-1,dr-9-2` when an initiative bundles rows. Appears as the bold closing parenthesis at the end of the bullet.

If any of the three are missing, ask the user to supply them with a one-line example.

## Refusal conditions (check these FIRST, before any file edits)

Refuse and report cleanly — do NOT produce a partial diff — if any of the following hold:

| Condition | Message |
|---|---|
| Active `state.json` cannot be located OR `schemaVersion != 2` | `"Refusing: unsupported backlog-sweep state schema. This skill requires schemaVersion == 2. Found: <value> at <path>."` |
| `gh` CLI is not on PATH (`command -v gh` fails) | `"Refusing: the 'gh' CLI is required to read commit metadata but was not found on PATH."` |
| An existing bullet under `## [Unreleased]` already contains one of the supplied row ids (simple substring scan for the backlog id in backticks, e.g. `` `changelog-entry-draft-from-pr-metadata` ``) | `"Refusing: an Unreleased entry for row '<id>' is already present on line <N>. This skill never clobbers existing entries — edit the existing bullet by hand if it needs an update, or remove it first."` |
| The initiative id is not found in `state.json` OR its `changelogCategory` is null | `"Refusing: initiative '<id>' not found in <state.json path>, or its changelogCategory is null. Backfill changelogCategory in state.json first."` |
| The commit cannot be resolved (`git show <ref>` returns non-zero) | `"Refusing: cannot resolve commit / branch '<ref>'."` |

Refusals must be emitted BEFORE any file is touched. Never emit a partial CHANGELOG edit.

## Workflow

### Step 1: Locate the active state.json

Find the most recent `ai_docs/plans/<timestamp>_backlog-sweep/state.json`:

```bash
ls -t ai_docs/plans/*_backlog-sweep/state.json | head -1
```

Read it. Confirm `schemaVersion == 2`. Locate the initiative whose `id` matches the first argument. Capture:

- `changelogCategory` — one of `"Fixed"`, `"Added"`, `"Changed"`, `"Changed — BREAKING"`, `"Maintenance"`.
- `testFilesAdded` (int, may be 0).
- `rowsClosedCount` (int).

### Step 2: Read the commit body

Via `gh` (preferred, pulls trailers and merged-PR context) or `git` fallback:

```bash
git show -s --format=%B <commit-sha-or-branch>
```

The **subject line** is the commit title (e.g. `feat(skills): add /draft-changelog-entry skill (changelog-entry-draft-from-pr-metadata)`). The **body** starts after the first blank line and carries the explanatory prose + row-id citations. Adapt the body into 1-3 sentences that mirror the format of existing `## [Unreleased]` bullets (see Step 4 for the format). Keep proper-nouns, tool names, and file paths verbatim; compress rationale prose.

### Step 3: Scan for duplicates under `## [Unreleased]`

Before emitting any diff, confirm that **none** of the supplied row ids already appear in backticks under the `## [Unreleased]` block. If any match, refuse per the table above.

### Step 4: Draft the bullet

Format (mirrors existing entries in `CHANGELOG.md`):

```
- **<Category>:** <one-sentence summary of the change> (`<row-id-1>`[, `<row-id-2>`, ...]).
```

When the commit body has multiple paragraphs of rationale (common for non-trivial fixes), include the second sentence only if it carries load-bearing context (root cause, observable symptom, regression-test name). Do NOT dump the whole commit body — the CHANGELOG is a summary, not an archive. The commit history is the archive.

- Category maps from `changelogCategory`: `"Fixed"` → `### Fixed`, `"Added"` → `### Added`, `"Changed"` → `### Changed`, `"Changed — BREAKING"` → `### Changed — BREAKING`, `"Maintenance"` → `### Maintenance`.
- The bold `**<Category>:**` prefix matches the in-repo convention even inside the `### Maintenance` subsection (e.g. `**Backlog:** 6 rows closed`).

### Step 5: Insert under the right subsection

Locate `## [Unreleased]` in `CHANGELOG.md`. Under it, find the subsection that matches the category (`### Fixed` / `### Added` / `### Changed` / `### Changed — BREAKING` / `### Maintenance`). Insert the new bullet as the FIRST item under that subsection (newest-first, matching existing in-file ordering).

If the target subsection does not yet exist (it sometimes happens that `### Changed — BREAKING` is absent when no breaking changes have accumulated), insert the subsection header in the canonical order: `### Fixed` → `### Changed — BREAKING` → `### Changed` → `### Added` → `### Maintenance`.

### Step 6: Update Maintenance tallies (only if `### Maintenance` already exists under `## [Unreleased]`)

If a `### Maintenance` subsection is already present AND either `testFilesAdded > 0` or `rowsClosedCount > 0`, amend the existing `**Backlog:** N rows closed` / tests-added tally bullets atomically with the insertion above:

- `**Backlog:** N rows closed under the <date> sweep ...` — sum `N` += `rowsClosedCount` and append each new row id to the comma-separated list of closed row ids at the end of that bullet (preserving existing citations).
- `**Tests:** N tests added` (if present) — sum += `testFilesAdded`.

If the `### Maintenance` subsection exists but the specific tally bullet you need to update does not yet appear, leave it alone — only adjust existing bullets, never synthesize new Maintenance bullets (a human picks the wording).

If the category is `"Maintenance"` itself (i.e. the current change IS a maintenance entry), emit the new bullet under `### Maintenance` per Step 5 and do NOT also recount — the bullet you inserted already carries its own tally.

### Step 7: Emit the diff and stop

Print the `CHANGELOG.md` diff (unified, 3 lines of context) to the transcript for the user's review. Do NOT run `git add`, `git commit`, or any other mutating git command. Report:

- Which `## [Unreleased]` subsection the bullet landed under.
- Whether Maintenance tallies were updated (and by how much).
- Next step: `"Review the diff. When ready, run /ship (or git commit) to ship."`

## Example output

**Input:** `/draft-changelog-entry changelog-entry-draft-from-pr-metadata remediation/changelog-entry-draft-from-pr-metadata changelog-entry-draft-from-pr-metadata`

**Lookup from `state.json`:** `changelogCategory = "Added"`, `rowsClosedCount = 1`, `testFilesAdded = 0`.

**Commit body (abbreviated):**
> Add `skills/draft-changelog-entry/SKILL.md` — a prompt document that takes (initiative id, commit SHA, backlog row id(s)) and emits one `## [Unreleased]` CHANGELOG bullet plus atomically updates Maintenance tallies. Non-destructive: prints a diff, does not commit. Refuses on schemaVersion != 2, missing gh, or duplicate row-id.

**Emitted diff:**

```diff
 ## [Unreleased]

 ### Fixed

 ...

+### Added
+
+- **Added:** `/draft-changelog-entry` skill takes an initiative id + commit SHA and emits the CHANGELOG bullet + Maintenance tallies atomically (`changelog-entry-draft-from-pr-metadata`).
+
 ### Maintenance

 - **Backlog:** 6 rows closed under the 2026-04-17 sweep ...
```

**Report:**
> Inserted bullet under `## [Unreleased]` → `### Added`. Maintenance tallies unchanged (rowsClosedCount=1 but the change itself is in `### Added`, not `### Maintenance`). Review the diff. When ready, run /ship.
