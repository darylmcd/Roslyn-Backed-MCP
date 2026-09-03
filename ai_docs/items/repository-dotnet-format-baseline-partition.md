# repository-dotnet-format-baseline-partition — Partition the repository formatter baseline

**row:** `repository-dotnet-format-baseline-partition` · **pri:** `Low` · **size:** `M` · **deps:** `ci-router-runner-label-duplication`

## Anchors

- `.editorconfig`
- `CI_POLICY.md`
- `.github/workflows/ci.yml`
- `RoslynMcp.slnx`
- New `eng/verify-changed-format.ps1`
- New `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`

## Acceptance

- [ ] Capture the current `dotnet format RoslynMcp.slnx --verify-no-changes --no-restore` failures as a deterministic diagnostic/file inventory without suppressing or relabeling violations.
- [ ] Transactionally file bounded repair children (at most four production files, three test files, and one formatter regression shape per row) for every baseline file before this inventory row closes.
- [ ] Add a changed-file formatter gate that fails on new `IMPORTS`, `IDE1006`, or `FINALNEWLINE` violations while untouched baseline debt remains explicitly tracked.
- [ ] One process-level outcome matrix proves clean changed files pass, changed files carrying each diagnostic fail, and an unchanged baseline violation does not conceal a newly introduced failure.

## Evidence

- A 2026-08-14 full formatter run reported 337 failures across 181 unique files: 110 `IMPORTS` files, 226 `IDE1006` findings across 112 files, and one `FINALNEWLINE` test-file finding.
- Touched-file formatter checks for the SDK 2.x correction are green, so this row owns pre-existing repository-wide debt rather than weakening the current change's validation.
## Amendment — 2026-08-25 (backlog-sweep 20260825T151721Z; PRs #1347, #1356 — row stays OPEN)

Two child initiatives shipped: the baseline inventory + contract test (#1347) and the changed-file gate (#1356).

**Acceptance bullet 2 remains OPEN and was deliberately out of scope** — "transactionally file bounded repair children for every baseline file" is row-factory work, routed to `/discovery-sweep` rather than a sweep stanza.

**The inventory numbers in this file were stale; corrected from a live run.** The 2026-08-14 figure of 337 failures across 181 files is superseded by a regeneration at `origin/main` on 2026-08-25:
**418 findings across 232 files — IDE1006 306, IMPORTS 108, WHITESPACE 3, FINALNEWLINE 1.**

**Operational trap discovered while landing #1347 — read before touching this row again.** `eng/format-baseline.json` is **base-sensitive**: it records the repository's live violation set, so any merge to `main` that adds a violating file makes a previously-tracked baseline stale and trips `FormatterBaselineContractTests.Generator_IsDeterministicAndTheTrackedInventoryCoversTheLiveRunAsync`. #1347 failed CI on exactly this — three test files created by sibling initiatives in the same sweep. **Regenerate the inventory as the last commit before landing anything that touches it.** The generator takes no path argument (the `-OutputPath` parameter was removed as unrequested scope); verify with `-Check`.

**Root cause of the dominant debt class is now tracked separately.** IDE1006 is 306 of the 418 findings because `.editorconfig`'s private-field naming rule sets `applicable_kinds = field` with no `required_modifiers`, so it demands a `_` prefix on `private const` and `private static readonly` too — against a codebase that uses PascalCase consts. Do **not** repair baseline files by renaming consts to `_camelCase`; fix the rule scoping first. Row: `editorconfig-private-field-naming-rule-overbroad`.

## Amendment — 2026-09-02 (measured on a clean tree)

`eng/format-baseline.json` is stale in the SHRINKING direction, so
`pwsh -NoProfile -File ./eng/generate-format-baseline.ps1 -Check` exits 1 on an unmodified checkout.

Measured at commit `3fcd132b`, base and patched trees identical: the live run emits
`findingCount=117 / fileCount=112 / diagnosticIds=[FINALNEWLINE, IDE1006, IMPORTS]` while the tracked
artifact records `121 / 114` and still declares `WHITESPACE`. Repairs landed without regenerating, so
the ratchet is loose by 4 findings and 2 files.

`FormatterBaselineContractTests` stays green because its subset assertions deliberately tolerate
shrink drift — which is why this went unnoticed. The practical cost is that the script's exit code is
useless as a signal to any caller other than that test.

Fold into this row's repair slices: regenerate the artifact so `-Check` exits 0 on a clean tree, or
state explicitly that non-zero-on-shrink is intended and pin whichever contract is chosen.

[source: 2026-09-02 backlog-remediate PR #1429 cold review]
