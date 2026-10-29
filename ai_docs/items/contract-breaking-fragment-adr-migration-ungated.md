# contract-breaking-fragment-adr-migration-ungated — Gate Changed — BREAKING fragments on a migration note and an existing ADR

**row:** `contract-breaking-fragment-adr-migration-ungated` · **pri:** `Low` · **size:** `M`

## Anchors

- `eng/verify-changelog-fragments.ps1:27`
- `eng/verify-breaking-version-bump.ps1:104`
- `tests/RoslynMcp.Tests/BreakingVersionGateTests.cs:1`

## Acceptance

- [ ] `verify-changelog-fragments.ps1` rejects a `Changed — BREAKING` fragment that has no `Migration:` text and no `ADR NNNN` reference.
- [ ] An `ADR NNNN` reference must resolve to `docs/decisions/NNNN-*.md`; a dangling number is rejected.
- [ ] Tests cover the pass, missing-migration and dangling-ADR cases (red on the current validator).

## Evidence

- `AGENTS.md` § Breaking-change posture and `docs/release-policy.md:23-28,70` require an ADR plus a migration note for every breaking change.
- `eng/verify-changelog-fragments.ps1:27` checks category vocabulary only; `eng/verify-breaking-version-bump.ps1:104,192` checks the major bump only; `rg -n 'migration|decisions'` over both → no hit.
- `CHANGELOG.md` 5.0.0: 18 BREAKING bullets, each with a migration or ADR reference, kept by hand.
- refactor-audit 20261028-1500 at f8734a2f, finding `contract-breaking-fragment-adr-migration-ungated` (fingerprint 997cf838a6); tier 0.

## Context

- regression_shape: validator test with a BREAKING fragment lacking both markers fails.
- Counterargument weighed: author discipline has held on all 18 entries and a regex cannot judge note quality. The policy is mandatory for a published contract, so the missing machine check is the gap; keep the check to presence and ADR resolution.
