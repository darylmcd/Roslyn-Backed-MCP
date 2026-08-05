# stage-review-inbox-multisession-retro-glob-miss — fix the retro-report discovery glob in stage-review-inbox.ps1

**row:** `stage-review-inbox-multisession-retro-glob-miss` · **pri:** `Medium` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `eng/stage-review-inbox.ps1:133-138` (`$filePatterns` array)

## Acceptance

- [ ] `$filePatterns` includes a pattern that matches `*_roslyn-mcp-multisession-retro.md` (either add it alongside the existing `*_roslyn-mcp-retro.md` entry, or replace both with a single pattern that covers both shapes, e.g. `*_roslyn-mcp*retro.md` — verify this doesn't accidentally match unrelated filenames first)
- [ ] `pwsh -File eng/stage-review-inbox.ps1 -DryRun` run against this repo's `ai_docs/reports/` lists all 3 existing multisession-retro reports as discoverable
- [ ] The docstring's recognized-shapes list (lines ~28-31) and its filename example are updated to match

## Evidence

- Self-discovered during `/backlog-intake` (2026-08-05): the skill's Phase 0 staging step failed to find `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`, requiring a manual `cp`/`mv` workaround before triage could proceed. Verified all 3 retro reports this repo has ever produced (`20260521T043918Z`, `20260608T203050Z`, `20260805T210025Z`, all suffixed `_roslyn-mcp-multisession-retro.md`) would be missed by the current `'*_roslyn-mcp-retro.md'` pattern — none contain that exact literal substring.

## Context

`stage-review-inbox.ps1`'s docstring and `$filePatterns` array both use the short form `*_roslyn-mcp-retro.md`, but the actual retro-generation prompt this repo uses (embedded in the `/backlog-sweep`-adjacent retro workflow, not this repo's own tooling) has always produced the longer `*_roslyn-mcp-multisession-retro.md` suffix. The mismatch means every `/backlog-intake` run against this repo's own retro output has silently required a manual staging workaround since the pattern was written — the discovery step reported "no new artifacts found" against a repo that in fact had unstaged retro evidence sitting in `ai_docs/reports/`.

## Notes

Check whether any SIBLING repo's retro-generation convention also uses the short `*_roslyn-mcp-retro.md` form (in which case both patterns should stay, not just be replaced) before narrowing the fix to a single merged pattern.
