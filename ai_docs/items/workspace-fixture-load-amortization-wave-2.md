# workspace-fixture-load-amortization-wave-2 — Reduce owned cache and mutation fixture setup

**row:** `workspace-fixture-load-amortization-wave-2` · **pri:** `Low` · **size:** `S`

## Anchors

- tests/RoslynMcp.Tests/TestInfrastructure/OwnedRefactoringTestBase.cs
- tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs
- tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs

## Acceptance

- [x] Give ProjectMutationIntegrationTests per-invocation production services and fresh fixtures; load only SampleLib for self-contained project edits. Retain full graphs for reference and central-package cases.
- [x] Preserve all 30 project-mutation methods / 33 expanded cases, including byte fidelity, undo, reload, refusal and outside-workspace consumer assertions.
- [ ] Re-derive CompilationCacheAdoptionTests fixture needs; use smaller fresh fixtures where sufficient and retain graph/lifecycle-specific cases. Do not replace explicit cross-project cache coverage with a single project.
- [ ] Measure repeated Windows/Linux execution for each migrated suite and run full CI; preserve case-occurrence identity and skip reasons.
- [ ] If a reusable mutable fixture is introduced, prove complete byte/project/cache/token/undo restoration and randomized-order repeated execution. Fresh owned fixtures do not need a reset protocol.

## Evidence

- Historical audit: ai_docs/reports/20261017-test-efficiency/report.md.
- PR #1792 hosted run 39074897621: ProjectMutationIntegrationTests is the remaining highest-cost class on both OSes; attempt 1 Windows 340.220s / Linux 221.034s; attempt 2 Linux 308.856s.
- The inherited class-reset proposal is superseded by smaller fresh fixtures for mutation cases. Each test owns its production services and temp root; no state is reused between invocations.
- Project-mutation implementation: 21 methods use SampleLib; nine methods retain SampleSolution. All original method/data/semantic tokens match after the explicit fixture/service substitutions.
- CompilationCacheAdoptionTests remains pending; this row stays open.
