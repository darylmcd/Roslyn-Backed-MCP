---
name: bump
description: "Version bump. Use when: bumping the version for a release, preparing a version increment (major/minor/patch), or when code changes require a new version. Takes bump type as input: 'major', 'minor', or 'patch'. Edits all 5 version files and prepends a CHANGELOG.md section."
user-invocable: true
argument-hint: "patch | minor | major"
---

# Version Bump

You are a release engineer. Your job is to increment the project version across all 5 version files and prepare a changelog section.

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
| 5 | `CHANGELOG.md` | New `## [X.Y.Z] - YYYY-MM-DD` header at top of entries |

## Workflow

### Step 1: Read Current Version

Read `Directory.Build.props` and extract the current `<Version>` value. Display it to the user.

### Step 2: Compute New Version

Parse the current version as `major.minor.patch`. Apply the bump type:
- `patch`: increment patch
- `minor`: increment minor, reset patch to 0
- `major`: increment major, reset minor and patch to 0

Display the new version and confirm with the user before proceeding.

### Step 3: Edit All 5 Files

Edit each file using the Edit tool, replacing the old version with the new version:

1. **`Directory.Build.props`**: Replace `<Version>OLD</Version>` with `<Version>NEW</Version>`
2. **`.claude-plugin/plugin.json`**: Replace `"version": "OLD"` with `"version": "NEW"` (the first occurrence)
3. **`.claude-plugin/marketplace.json`**: Replace `"version": "OLD"` in the `plugins[0]` entry (NOT the `metadata.version` on line 9)
4. **`manifest.json`**: Replace `"version": "OLD"` with `"version": "NEW"`
5. **`CHANGELOG.md`**: Insert a new section header `## [NEW] - YYYY-MM-DD` (today's date) with empty `### Fixed`, `### Changed`, `### Added` subsections above the previous top entry. Do NOT remove or edit existing entries.

### Step 4: Verify

Run `eng/verify-version-drift.ps1` via Bash to confirm all 5 files agree on the new version. If it fails, fix the discrepancy and re-run.

### Step 5: Report

Display a summary:
- Previous version → New version
- Files modified (list all 5)
- Reminder: "Fill in the CHANGELOG.md section before shipping. Run `/roslyn-mcp:publish-preflight` when ready to validate the full release."
