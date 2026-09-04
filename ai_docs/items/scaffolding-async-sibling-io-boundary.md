# scaffolding-async-sibling-io-boundary — Make sibling IO cancellation-aware

**row:** `scaffolding-async-sibling-io-boundary` · **pri:** `Low` · **size:** `M` · **deps:** `sibling-test-name-discovery-warning, scaffold-sibling-file-enumeration-consolidation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/BatchScaffoldingTests.cs`

## Acceptance

- [ ] Async scaffold preview paths do not perform synchronous sibling file reads or long directory enumeration.
- [ ] One injectable boundary provides cancellation-aware enumeration/read behavior to single and batch scaffolding.
- [ ] Expected IO failures retain the established redacted warning policy; cancellation and unexpected exceptions propagate.

## Regression

Cancel during an injected sibling read and assert `OperationCanceledException` propagates without a preview token, partial disk output, or a redacted-warning downgrade.


> Test-anchor correction (2026-09-04): `tests/RoslynMcp.Tests/BatchScaffoldingTests.cs` does not exist. Use existing batch coverage in `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`; no new test file is intended.
