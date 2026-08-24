# test-base-static-service-locator-decomposition — Decompose the TestBase static service locator

**row:** `test-base-static-service-locator-decomposition` · **pri:** `Medium` · **size:** `S` · **deps:** `test-assembly-cleanup-failure-observability,mcp-roots-fixture-lifecycle-consolidation`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/TestServiceContainer.cs`
- `tests/RoslynMcp.Tests/SharedWorkspaceTestBase.cs`

## Acceptance

- [ ] Replace the dozens of mutable static service properties with one immutable, assembly-owned fixture context constructed from `TestServiceContainer`.
- [ ] Separate repository fixture paths and assembly lifecycle ownership from service lookup so initialization and disposal have one explicit owner.
- [ ] Preserve the assembly-shared workspace behavior and source-compatible class cleanup boundary while migrating consumers incrementally or atomically.
- [ ] Add one parallel two-class regression proving both classes receive the same initialized context and that its owned resources are disposed exactly once.

## Evidence

- `TestBase` currently mixes assembly initialization, environment binding, repository path discovery, MCP server ownership, disposal, and more than sixty mutable static service properties; every new service extends the assignment list and obscures ownership.
2026-08-24 current evidence: `TestServiceContainer` resolves to `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`, not the stale root-level path implied by older notes. The pre-refactor Windows profile also measured a 10m17s serialized tail and 123 `[DoNotParallelize]` occurrences; use current semantic ownership rather than the stale anchor when planning.
