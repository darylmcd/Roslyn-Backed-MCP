# test-discovery-sweep-observability-test-gaps — Close test and doc gaps left by the sweep-observability fix

**row:** `test-discovery-sweep-observability-test-gaps` · **pri:** `Low` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `tests/RoslynMcp.Tests/TestDiscoverySweepObservabilityTests.cs`
- `tests/RoslynMcp.Tests/TestDiscoveryFileIdentityTests.cs`
- `src/RoslynMcp.Core/Models/RelatedTestsForFilesDto.cs`

## Acceptance

- [ ] A test where only the inbound-reference sweep cancels (the forced-fallback DataRow currently cancels in the direct sweep first).
- [ ] RelatedTestsDiagnosticsDto states that direct-reference is omitted from heuristicsAttempted when it fails for any input file (mixed completed/failed files).
- [ ] TestDiscoveryFileIdentityTests.DiscoveryFixture (Projects.Single(), builds a new service per access) and MultiProjectFixture share one scaffold; Service no longer rebuilds the service on each access.
- [ ] Remove the dead try/catch in CollectNamespaceNeighborTerms/AddNamespaceNeighborMatches or prove it can throw.

## Evidence

- Cold review of PR 2052 and PR 2048 follow-ups.
