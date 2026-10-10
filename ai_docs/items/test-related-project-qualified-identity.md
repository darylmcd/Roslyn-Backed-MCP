# test-related-project-qualified-identity — Preserve distinct tests across projects

**row:** `test-related-project-qualified-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:100-112`
- `tests/RoslynMcp.Tests/TestDiscoverFqdnDriftTests.cs`

## Acceptance

- [ ] Carry owning-project identity through symbol/file matching and enrichment; equal test FQNs in separate projects remain distinct with their correct ProjectName/FilePath/trigger metadata.
- [ ] Remove duplicate-key failures and silent first-project attribution without changing real test-run FQN spelling or collapsing distinct matches to avoid the error.
- [ ] Real two-project regression declares the same namespace/type/method FQN in both projects; symbol and file routes preserve both appropriate matches, project attribution and pagination while single-project controls remain unchanged.
- [ ] Re-derive same-mechanism collectors and projections; use existing DTO fields when complete, and follow public contract-care requirements if a public change is actually necessary.

## Evidence

- Traced on immutable c087ac4f512f2ddcddd83b89aca4e3e7c611757f; no runtime reproduction claimed. BuildFullyQualifiedTestName uses `var namespacePrefix = GetEnclosingNamespace(method) ?? projectName;`, so explicit identical namespaces/types/methods produce the same FQN across separate assemblies.
- File result projection at :627-632 uses `.ToDictionary(x => x.FullyQualifiedName, x => x.ProjectName, StringComparer.Ordinal)` and `.ToDictionary(t => t.FullyQualifiedName, StringComparer.Ordinal)`. Two discovered projects with that valid same FQN produce duplicate dictionary keys before the response is built.
- Symbol enrichment at :104-106 groups only FQN and selects `g.First().ProjectName`; collected matching dictionaries at :216/:257 and related-file trigger keys at :482/:572/:604/:608 similarly discard owning-project identity. The result can silently attribute a surviving test to the wrong project even when no dictionary throws.

## Context

- Different mechanism from test-discovery-file-path-case-identity: filesystem comparison versus assembly/project-qualified test identity. Not added to the current selected plan or implemented by the active path-identity leg.
- Recommended scope is one service plus focused public-behavior coverage, with actual seams/companions re-derived before execution. Existing DTO already carries ProjectName and FilePath; do not add a new public field merely to avoid carrying internal identity correctly.
