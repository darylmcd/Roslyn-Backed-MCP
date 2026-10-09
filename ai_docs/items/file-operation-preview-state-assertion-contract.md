# file-operation-preview-state-assertion-contract — Assert redeemability through a test seam

**row:** `file-operation-preview-state-assertion-contract` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FileOperationIntegrationTests.cs:333-339`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs:519-532`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs:1857`

## Acceptance

- [ ] Assert that refused operations leave no redeemable preview through an explicit test-supported behavioral seam. Remove dependence on private BoundedStore._entries representation.
- [ ] Update every AssertNoStoredPreviews consumer and preserve fail-loud assertion diagnostics, workspace isolation and refusal regression coverage.
- [ ] A store representation change alone must not break a correct behavioral assertion. Do not introduce a production compatibility accessor solely to retain reflection.

## Evidence

- Immutable base `683dc0fcb503d02f49063e6909e327272ab4044c`, FileOperationIntegrationTests:333-339 reflects private `_entries`, assumes IReadOnlyDictionary representation and uses null-forgiving assertions on reflection results.
- Existing `test-private-field-reflection-helper` covers other private getters; this row tracks the distinct preview-behavior representation dependency.

## Context

- Re-vet before lifecycle store representation changes; if that diff exposes this dependency, fix it in the same complete change and include all consumers in Scope.
