# changelog.d — Changelog fragments

Every initiative (or standalone PR) that would historically have appended a bullet to `CHANGELOG.md` under `## [Unreleased]` now writes a single fragment file here instead. At release-cut time the `/bump` skill consumes all fragment files, groups them by category, emits them into the new `## [X.Y.Z]` section of `CHANGELOG.md`, and deletes the consumed files in the same commit.

## Why

Parallel-subagent backlog-sweeps routinely hit `CHANGELOG.md` merge conflicts because every PR appends to the same `## [Unreleased]` subsection. The 2026-04-17 pass-1 session showed 50% of parallel subagents paying a serial-rebase tax on this one file — each rebase cost 1–3 minutes of conflict resolution plus a destructive-resolution risk (`"accept ours"` silently drops a sibling entry).

Per-PR-per-file fragments are merge-safe by construction: no two PRs ever touch the same file.

## File shape

- **Filename:** `<backlog-row-id>.md` — kebab-case, matches the backlog row the change closes. If a single PR closes multiple rows, pick one id as the filename; cite all closed rows in the bullet body.
- **Format:** YAML frontmatter carrying the CHANGELOG category; body is a single bullet in the shipping `**<Category>:**` prose style.

Example fragment, filename `changelog.d/release-cut-atomic-skill-bump-ship-tag-reinstall.md`:

    ---
    category: Added
    ---

    - **Added:** `/release-cut` skill at `.claude/skills/release-cut/SKILL.md` — single-invocation release pipeline (bump → verify → ship → tag → reinstall-both-layers). Closes `release-cut-atomic-skill-bump-ship-tag-reinstall`.

Note: the example above is indented by four spaces to render as a literal block in this README. Actual fragment files are not indented — they are plain YAML frontmatter + a markdown bullet at column 0.

## Valid categories

- `Fixed`
- `Changed`
- `Changed — BREAKING`
- `Added`
- `Maintenance`

Categories must match exactly (case-sensitive, including the em-dash `—` in `Changed — BREAKING`) — the `/bump` skill's fragment consumer groups by this field and refuses unknown values.

## Bump-time behavior

At release-cut (`/bump <type>`):

1. Scan `changelog.d/*.md` (excluding this README).
2. Group fragments by `category` frontmatter.
3. Emit under the new `## [X.Y.Z] - YYYY-MM-DD` section, one subsection per category, canonical order: `Fixed` → `Changed — BREAKING` → `Changed` → `Added` → `Maintenance`.
4. `git rm` every consumed fragment in the same commit that creates the `[X.Y.Z]` section — so after a bump, `changelog.d/` holds only this README again.

Malformed fragments fail the bump skill loudly — no silent skip. Failure modes:

- Missing or unparseable YAML frontmatter.
- Missing `category` key in frontmatter.
- Unknown category value (not one of the five listed above).
- Empty body (frontmatter-only file).

Two initiatives colliding on the same filename is rare (usually a bundled-work case) — the bump skill disambiguates by appending `-2`, `-3` suffixes at emit time; the filename-on-disk precedent wins and the second writer's `-2` file ships alongside.

## Migration note

This directory is the new home for Unreleased-section bullets. `## [Unreleased]` in `CHANGELOG.md` remains as a structural anchor but will stay empty between releases — bump-time consumption is what populates the new `## [X.Y.Z]` block. The `/draft-changelog-entry` skill writes directly into `changelog.d/` and does not touch `CHANGELOG.md`.
