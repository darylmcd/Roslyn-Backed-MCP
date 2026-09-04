# test-private-field-reflection-helper — Consolidate private-field test assertions

**row:** `test-private-field-reflection-helper` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PrivateFieldAssertions.cs` (new)
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs` (`GetRequiredPrivateField`)
- `tests/RoslynMcp.Tests/ServiceCollectionExtensionsTests.cs` (`AssertPrivateFieldNotNull`)

## Acceptance

- [ ] One helper owns non-public instance-field lookup, missing-field assertion, and non-null-value assertion.
- [ ] Both test classes use it; local reflection helpers and redundant `System.Reflection` imports are removed.

## Regression

A populated private collaborator is returned through the shared helper, while a misspelled field fails with the declaring type and requested field name.
