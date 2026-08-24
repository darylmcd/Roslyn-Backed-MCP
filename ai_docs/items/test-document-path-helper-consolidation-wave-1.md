# test-document-path-helper-consolidation-wave-1 — Centralize document lookup semantics

**row:** `test-document-path-helper-consolidation-wave-1` · **pri:** `Low` · **size:** `S` · **deps:** `test-base-static-service-locator-decomposition`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/AnalysisToolsTests.cs`
- `tests/RoslynMcp.Tests/AliasToolsTests.cs`

## Acceptance

- [ ] Add one workspace-explicit helper that uses the repository path comparer and fails with distinct diagnostics for zero and multiple filename matches.
- [ ] Migrate only the two anchored consumer suites and remove their private copies.
- [ ] Do not add another mutable static service/property to `TestBase`; align with the fixture-context decomposition dependency.
- [ ] Regress unique, missing, and ambiguous document names.

## Evidence

At least eight suites independently scan the current solution by filename with slightly different failure behavior. The duplication hides ambiguous-name assumptions and makes path-comparison fixes drift across consumers; this is the first bounded adoption wave.
