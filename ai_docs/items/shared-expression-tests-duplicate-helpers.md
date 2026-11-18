# shared-expression-tests-duplicate-helpers — Deduplicate shared-expression test helpers

**row:** `shared-expression-tests-duplicate-helpers` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/ExtractSharedExpressionBindingTests.cs:547`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionNamespaceIdentityTests.cs:339`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionValueContextTests.cs:626`

## Acceptance

- [ ] One helper set (compilation-clean assertion, compilation getter, preview call) shared by the Binding, NamespaceIdentity and ValueContext classes; no behavioural test change.

## Evidence

- shared-expr report bad-code sketch: test-helper triplication (AssertNoCompilerErrors in Binding:547 and NamespaceIdentity:339; GetCompilationAsync/GetCleanCompilationAsync x3; PreviewAsync in Binding:561 and ValueContext:605).
