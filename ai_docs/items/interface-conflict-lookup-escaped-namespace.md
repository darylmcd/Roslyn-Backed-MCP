# interface-conflict-lookup-escaped-namespace — Use metadata identity for escaped interface namespace conflicts

**row:** `interface-conflict-lookup-escaped-namespace` · **pri:** `Low` · **size:** `M` · **verified:** `ddf4cc6d0f4d1ee8a165571c099820583f52eeb9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:443`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:238`
- `tests/RoslynMcp.Tests/CrossProjectInterfaceBindingIntegrationTests.cs`

## Acceptance

- [ ] Detect occupied escaped namespaces before generating a duplicate in every producer. Qualify affected tool tiers and error envelopes first.

## Evidence

- Early conflict lookups concatenate escaped source spelling. Metadata omits identifier escapes, leaving late compile checks to detect some duplicates.
