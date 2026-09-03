# self-hosted-runner-doc-shard-decision-stale-crossref — refresh timing-evidence cross-reference

**row:** `self-hosted-runner-doc-shard-decision-stale-crossref` · **pri:** `Low` · **size:** `S`

## Anchors

- `docs/self-hosted-runner.md` — "Timing evidence" section, line ~59.
- `CI_POLICY.md` — "Hosted Shard Weighting Decision" section (added in #1427), the now-authoritative status this cross-reference should point at.

## Acceptance

- [ ] `docs/self-hosted-runner.md`'s "Timing evidence" section no longer implies shard rebalancing is an open/future task by default; it states the calibration is closed per `CI_POLICY.md`'s "Hosted Shard Weighting Decision" and points there for the current decision + re-check trigger.
- [ ] The line "Uploaded per-leg TRX files remain the source for future balancing; do not treat a static case-count estimate as measured performance." is either removed (redundant with the closed decision) or reworded to describe re-balancing as conditional on the documented re-check trigger, not an assumed ongoing task.

## Evidence

- Found during a 2026-09-03 staleness check of the CI-policy doc cluster requested by the maintainer. Not factually wrong — a future re-check per the documented trigger would still use TRX evidence — just an unrefreshed cross-reference left over from before `CI_POLICY.md`'s "Hosted Shard Weighting Decision" section existed.

## Context

Cosmetic/low-severity doc-drift, not a functional or safety issue. `CI_POLICY.md` is the canonical validation/merge-gating contract (per its own header) and already carries the authoritative, up-to-date decision with an explicit re-check trigger (shard count changes, non-test work moves between legs, or achievable gain exceeds the noise band across 5+ green runs). `docs/self-hosted-runner.md` is explicitly scoped to "human self-hosted operations" (per `CI_POLICY.md`'s Ownership section) and duplicates some of the same historical timing evidence to explain why self-hosted was retired — that duplication is fine; only this one line reads as stale now that the calibration it describes as ongoing has been formally closed elsewhere.
