# client-root-boundary-test-and-comparer-followups — Cover UNC share roots and share one path comparer in the root boundary

**row:** `client-root-boundary-test-and-comparer-followups` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs:28`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] Windows-only test: a UNC share-root parent widens only to prefixed siblings.
- [ ] One shared FileSystemPathComparison/comparer is used by ConfiguredRootBoundary and SolutionDiscoveryHelper.
- [ ] Test path helper P() naming no longer implies drive-letter awareness it lacks.

## Evidence

- Drive-root prefix widening shipped in PR 1960; these were reviewer follow-ups.
