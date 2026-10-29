# extract-type-tests-temp-dir-leak — Queue temp directories for cleanup in older ExtractType_* tests (CRLF string-array fixtures)

**row:** `extract-type-tests-temp-dir-leak` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Every test that creates a fixture directory calls QueueDirectoryForCleanup; no leaked temp dirs after the class runs.

## Evidence

- Seen while implementing extract-type-override-modifier-stripped.
