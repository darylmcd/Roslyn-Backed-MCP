# surface-snapshot-stale-surface-audit — refresh the doc-audit live-surface snapshot

**row:** `surface-snapshot-stale-surface-audit` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `.ai-doc-audit.md:86`

## Acceptance

- [ ] `/surface-audit` run; refreshed Live-Surface table in `.ai-doc-audit.md` with a current measurement date and counts
- [ ] `README` live-surface line stays CI-guarded by `ReadmeSurfaceCountTests`

## Evidence

- `.ai-doc-audit.md` Known Gaps explicitly requests this before the next release; measure the live `ServerSurfaceCatalog` rather than editing its partial files. Source: 2026-06-04 discovery-sweep work-search.

## Context

The doc-audit live-surface snapshot is stale (2026-05-06) — tool/resource/prompt/skill counts are unverified for the current release (snapshot says 168 tools; backlog evidence puts the surface at ~173, server now v2.3.2). Run `/surface-audit` to refresh before the next release cut.
