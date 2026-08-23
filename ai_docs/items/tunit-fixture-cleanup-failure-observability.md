# tunit-fixture-cleanup-failure-observability — Surface contributor fixture cleanup failures

**row:** `tunit-fixture-cleanup-failure-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TUnitProjectClassificationTests.cs`
- `tests/RoslynMcp.Tests/TUnitFilterTranslationTests.cs`

## Acceptance

- [ ] Replace unconditional cleanup suppression with cleanup that preserves reset/delete attempts and surfaces failures after every step runs.
- [ ] Add an injected delete-failure regression proving the original cleanup exception remains observable.
- [ ] Keep fixture-owned paths bounded to the test run; never delete shared or repository roots.

## Evidence

- Contributor PR #1315 introduces broad cleanup catches in both TUnit fixture files while its active owner review still requests behavioral corrections. Preserve contributor ownership and track the cleanup defect independently.
