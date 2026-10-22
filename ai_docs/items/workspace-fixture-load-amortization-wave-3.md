# workspace-fixture-load-amortization-wave-3 — Narrow remaining cross-project fixtures safely

**row:** `workspace-fixture-load-amortization-wave-3` · **pri:** `Low` · **size:** `S`

## Anchors

- tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs
- tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs
- tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs

## Acceptance

- [x] Give scaffolding cases fresh owned production services; retain all 39 methods / 43 expanded cases and assertions.
- [x] Use SampleLib for type cases and a two-project solution for source-to-test cases; preserve the common watcher root, source reference and restored dependencies.
- [x] Remove fifteen redundant initial loads by preparing injected sources before the first load; retain post-apply reload and sampling snapshot boundaries.
- [ ] Re-derive CrossProjectRefactoringIntegrationTests fixture requirements and own each invocation; retain every project needed by cross-project semantic assertions.
- [ ] Measure each migrated suite repeatedly on Windows/Linux and pass full CI without deleting case occurrences or weakening deadlines.
- [ ] If mutable fixture reuse is introduced, prove complete file/project/cache/token/undo reset and randomized-order execution. Fresh owned fixtures need no reset protocol.

## Evidence

- Scaffolding slice: ai_docs/reports/20261017-test-efficiency/scaffolding-fixtures.md and scaffolding-fixture-map.json.
- Inherited reset-first proposal superseded by smaller fresh fixtures and removal of unobserved setup loads.
- Two unchanged local scaffolding baselines passed all 43 cases at 108.225090 and 107.757960 seconds.
- New graph regression fails against the old three-project fixture; corrected focused suites pass 56 cases with six existing platform skips.
- CrossProjectRefactoringIntegrationTests remains pending; this row stays open.
