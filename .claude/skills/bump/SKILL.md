---
name: bump
description: "Version bump. Use when: bumping the version for a release, preparing a version increment (major/minor/patch), or when code changes require a new version. Takes bump type as input: 'major', 'minor', or 'patch'. Edits all 5 version files, consumes `changelog.d/*.md` fragments into a new `## [X.Y.Z]` section grouped by category, and `git rm`s the consumed fragments."
user-invocable: true
argument-hint: "patch | minor | major"
---

# Version Bump

You are a release engineer. Your job is to increment the project version across all 5 version files, consume accumulated `changelog.d/` fragments into the new `## [X.Y.Z]` section of `CHANGELOG.md`, and delete the consumed fragments in the same commit-ready change set.

## Server discovery

This skill edits repository files. Roslyn MCP **`server_catalog`** is unrelated unless you are validating the shipped tool surface after a release.

## Input

`$ARGUMENTS` is the bump type: `patch`, `minor`, or `major`. If not provided, ask the user which type of bump to perform with a brief explanation:
- **patch** (1.11.1 -> 1.11.2): bug fixes, doc changes, no new features
- **minor** (1.11.1 -> 1.12.0): new features, new stable tools, backward-compatible changes
- **major** (1.11.1 -> 2.0.0): breaking changes to stable tool contracts

## Version Files

All 5 files must carry the same version string. See `docs/release-policy.md` § *Where To Bump The Version String* for the canonical reference.

| # | File | Field |
|---|------|-------|
| 1 | `Directory.Build.props` | `<Version>X.Y.Z</Version>` |
| 2 | `.claude-plugin/plugin.json` | `"version": "X.Y.Z"` |
| 3 | `.claude-plugin/marketplace.json` | `plugins[0].version` (NOT `metadata.version`) |
| 4 | `manifest.json` | `"version": "X.Y.Z"` |
| 5 | `CHANGELOG.md` | New `## [X.Y.Z] - YYYY-MM-DD` header populated from `changelog.d/*.md` fragments, grouped by category |

## Fragment-file migration

`CHANGELOG.md`'s `## [Unreleased]` section is a structural anchor and stays empty between releases. Per-PR changelog entries are written as fragment files at `changelog.d/<row-id>.md` with YAML frontmatter carrying the category (see `changelog.d/README.md` for the full pattern). At release-cut time this skill consumes those fragments.

## Workflow

### Step 1: Read Current Version

Read `Directory.Build.props` and extract the current `<Version>` value. Display it to the user.

### Step 2: Compute New Version

Parse the current version as `major.minor.patch`. Apply the bump type:
- `patch`: increment patch
- `minor`: increment minor, reset patch to 0
- `major`: increment major, reset minor and patch to 0

Display the new version and confirm with the user before proceeding.

### Step 3: Edit Version Files 1-4

First, create the release-managed-edit override sentinel so the PreToolUse guard (`eng/guard-release-managed-files.ps1`) allows the version-file edits:

```bash
touch .release-managed-edit-allowed
```

The sentinel is gitignored and has a 1800 s TTL. See `ai_docs/workflow.md` § Release-managed file guard.

Then edit each of the first four version files using the Edit tool, replacing the old version with the new version:

1. **`Directory.Build.props`**: Replace `<Version>OLD</Version>` with `<Version>NEW</Version>`
2. **`.claude-plugin/plugin.json`**: Replace `"version": "OLD"` with `"version": "NEW"` (the first occurrence)
3. **`.claude-plugin/marketplace.json`**: Replace `"version": "OLD"` in the `plugins[0]` entry (NOT the `metadata.version` on line 9)
4. **`manifest.json`**: Replace `"version": "OLD"` with `"version": "NEW"`

### Step 4: Consume `changelog.d/` fragments into `CHANGELOG.md`

Scan `changelog.d/*.md` (exclude `README.md` — the directory explainer is NOT a fragment):

```bash
ls changelog.d/*.md | grep -v README.md
```

For each fragment, parse YAML frontmatter and extract:

- `category` — must be one of: `Fixed`, `Changed`, `Changed — BREAKING`, `Added`, `Maintenance` (em-dash is U+2014). Unknown values fail the bump loudly — see *Refusal conditions* below.
- Body — everything after the closing `---`. Expected to be a single bullet in the shipping `**<Category>:**` prose style (one per fragment).

Group the fragments by `category` in the canonical emit order: `Fixed` → `Changed — BREAKING` → `Changed` → `Added` → `Maintenance`.

Insert a new section into `CHANGELOG.md` immediately above the most recent `## [X.Y.Z]` block:

```
## [NEW] - YYYY-MM-DD

### Fixed

<bullets from Fixed fragments in the order fragments were read>

### Changed — BREAKING

<bullets from Changed — BREAKING fragments>

### Changed

<bullets from Changed fragments>

### Added

<bullets from Added fragments>

### Maintenance

<bullets from Maintenance fragments>
```

Omit any subsection that has zero fragments (do NOT emit an empty `### Fixed` header if no Fixed fragments were present). Do NOT remove or edit `## [Unreleased]` — it stays as a structural anchor with its empty subsection headers.

If no fragments were present at all, emit a `## [NEW] - YYYY-MM-DD` section with a single `_No user-visible changes in this release._` line and proceed — some patch bumps legitimately ship with no fragments (e.g. version-file drift repair).

Delete each consumed fragment from disk after the `CHANGELOG.md` edit lands:

```bash
rm changelog.d/<row-id>.md
```

The intent is that `git add -- CHANGELOG.md changelog.d/` then `git status` shows a single atomic change set: one `CHANGELOG.md` edit + N `changelog.d/*.md` deletions. This is what `/ship` (or a manual `git commit`) will commit.

### Refusal conditions (fragment consumption)

Refuse loudly — abort the bump before any `CHANGELOG.md` write — if any of the following hold:

| Condition | Message |
|---|---|
| A fragment has missing or unparseable YAML frontmatter | `"Refusing: changelog.d/<file> is missing its YAML frontmatter or the frontmatter did not parse. Fix the fragment and re-run."` |
| A fragment's `category` key is missing | `"Refusing: changelog.d/<file> has no 'category' key in its frontmatter. Fix the fragment and re-run."` |
| A fragment's `category` value is not one of the five canonical values | `"Refusing: changelog.d/<file> has category '<value>'; expected one of Fixed / Changed — BREAKING / Changed / Added / Maintenance. Fix the fragment and re-run."` |
| A fragment has no body (frontmatter-only) | `"Refusing: changelog.d/<file> has no bullet body. Fix the fragment and re-run."` |

Do NOT silently skip malformed fragments — a silent skip would lose the release note.

### Step 5: Verify

Run `eng/verify-version-drift.ps1` via Bash to confirm all 5 files agree on the new version. If it fails, fix the discrepancy and re-run.

Also confirm `changelog.d/` now contains only `README.md` — every fragment that was present at Step 4 start should have been consumed and deleted.

### Step 6: Cleanup sentinel

Remove the override sentinel created in Step 3 so subsequent edits go through the normal guard:

```bash
rm -f .release-managed-edit-allowed
```

(It is gitignored, so a leftover sentinel does not pollute the commit, but removing it keeps the override contract honest.)

### Step 7: Report

Display a summary:
- Previous version → New version
- Files modified (list all 5 version files + `CHANGELOG.md`)
- Fragments consumed (count + list of `changelog.d/` filenames deleted)
- Reminder: "Review the `## [NEW]` section in `CHANGELOG.md` — the grouped bullets came directly from the fragments. Edit the prose if a fragment was under-specified. Run `/roslyn-mcp:publish-preflight` when ready to validate the full release."
