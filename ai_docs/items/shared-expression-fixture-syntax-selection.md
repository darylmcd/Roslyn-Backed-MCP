# shared-expression-fixture-syntax-selection — Derive existing shared-expression fixture positions

**row:** `shared-expression-fixture-syntax-selection` · **pri:** `Low` · **size:** `S` · **verified:** `9fa31b98e92d93a212c875450e99734706406d90`

## Anchors

- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`
- `tests/RoslynMcp.Tests/ExtractionArgumentRefusalWireTests.cs`

## Acceptance

- [ ] Cover the same fixture invocations and negative argument values using source-derived spans. Retain actual preview and apply assertions.

## Evidence

- The new namespace regression derives its fixture invocation span. Existing tests still pass fixed line and column positions into the same copied fixture, whose source comments call for syntax-based selection.
