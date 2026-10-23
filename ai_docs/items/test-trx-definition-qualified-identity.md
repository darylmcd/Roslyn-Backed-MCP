# test-trx-definition-qualified-identity — test-trx-definition-qualified-identity

**row:** `test-trx-definition-qualified-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`: ParseSingleTrxFile.
- `tests/RoslynMcp.Tests/DotnetOutputParserTrxIdentityTests.cs`.
- `tests/RoslynMcp.Tests/ForkInvocationManagerAuthorityTests.cs`: AssertOriginalTestBodyFailsAsync.

## Acceptance

- [ ] Resolve failed-result testId through this TRX document's unique TestDefinitions/UnitTest/TestMethod; retain class and actual method identity.
- [ ] Preserve parameterized display labels; equal display names from distinct classes remain separate failures.
- [ ] Retain conservative existing fallback for missing or ambiguous definitions and existing malformed XML behavior.
- [ ] Observe old-parser failure and fixed-parser success for focused controls and actual original test process.

## Evidence

- Restore prerequisite fixed15 actual-process fixture executed its intended assertion failure, but FullyQualifiedName was ReviewedValueIsExecuted instead of ScopedForkTests.ReviewedValueIsExecuted.
- ParseSingleTrxFile assigned UnitTestResult.testName directly to FullyQualifiedName and ignored TestDefinitions/TestMethod.
- Fix belongs to the active restore prerequisite because its real execution premise exposed this parser defect; do not advance the 15-item batch.
