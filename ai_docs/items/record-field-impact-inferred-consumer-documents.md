# record-field-impact-inferred-consumer-documents — Find inferred record consumers across documents

**row:** `record-field-impact-inferred-consumer-documents` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs:207-225`
- `tests/RoslynMcp.Tests/RecordFieldAddSatelliteTests.cs`

## Acceptance

- [ ] Report relevant inferred record deconstruction and with sites in a separate file that obtains the value from a factory. Preserve stable deduplication and cancellation behavior.

## Evidence

- Source review at `34a87d9c`. The inferred scan visits only documents already returned by explicit type-reference discovery, which excludes factory-only consumer files.
