# diagnostic-query-regression-fixture-extraction — Extract diagnostic query test fixtures

**row:** `diagnostic-query-regression-fixture-extraction` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`
- `tests/RoslynMcp.Tests/DiagnosticQueryTestFixtures.cs` (new)

## Acceptance

- [ ] Move analyzer references, analyzers, compilation-cache, and workspace-manager doubles into one focused fixture file.
- [ ] Keep diagnostic policy, version ordering, and cache-cap tests in the regression suite with unchanged behavioral coverage.
- [ ] The targeted `DiagnosticQueryServiceRegressionTests` suite passes before and after the extraction.

## Evidence

- The regression suite is 865 lines and embeds more than ten fake analyzer/workspace types below the behavioral tests; the cache-contract edit had to navigate through this mixed fixture ownership.

## Context

Keep the production service unchanged. Preserve private test-fixture visibility and do not introduce a shared test service locator.
