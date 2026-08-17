# repository-dotnet-format-baseline-partition — Partition the repository formatter baseline

**row:** `repository-dotnet-format-baseline-partition` · **pri:** `Low` · **size:** `M` · **deps:** `ci-router-runner-label-duplication`

## Anchors

- `.editorconfig`
- `CI_POLICY.md`
- `.github/workflows/ci.yml`
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
