# upgrade-reference-static-count-date-drift — Remove stale upgrade reference snapshots

**row:** `upgrade-reference-static-count-date-drift` · **pri:** `Low` · **size:** `S`

## Anchors

- `docs/upgrade-matrix.md` — opening snapshot date
- `justfile` — verify-version-drift recipe comment

## Acceptance

- [ ] Replace the stale fixed inventory date with guidance to use the current checked package inventory.
- [ ] Remove the duplicated version-source count from the recipe comment and point to the canonical verifier.
- [ ] Verify documentation and version parity without changing executable recipe behavior.

## Evidence

- The matrix claims its current values reflect 2026-08-24 even as centrally checked package rows change in subsequent PRs.
- The recipe comment says six version files while the canonical workflow and verifier enumerate seven version sources.
