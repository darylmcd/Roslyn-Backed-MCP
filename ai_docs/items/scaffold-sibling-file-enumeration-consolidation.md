# scaffold-sibling-file-enumeration-consolidation — Consolidate sibling test discovery

**row:** `scaffold-sibling-file-enumeration-consolidation` · **pri:** `Low` · **size:** `S` · **deps:** `sibling-test-name-discovery-warning`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs` (`FindMostRecentSiblingTestFile`, `CollectSiblingTestMethodNames`)
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] One helper owns sibling enumeration, destination/bin/obj filtering, recursion, and LastWriteTime ordering for both consumers.
- [ ] Most-recent pattern inference and sampled-name collection retain their distinct projections without duplicating filesystem rules.

## Regression

A fixture with destination, bin/obj, nested, older, and newer candidates produces identical ordered eligible files for both consumers before their distinct projection steps.
