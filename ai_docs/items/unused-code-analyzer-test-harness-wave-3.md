# unused-code-analyzer-test-harness-wave-3 — Retire the private workspace-manager fakes in the duplicate-method, code-metrics and compilation-cache tests (wave 3 of 3)

**row:** `unused-code-analyzer-test-harness-wave-3` · **pri:** `Low` · **size:** `S` · **deps:** `unused-code-analyzer-test-harness-wave-1`

## Anchors

- `tests/RoslynMcp.Tests/DuplicateMethodDetectorTests.cs:887-1053`
- `tests/RoslynMcp.Tests/CodeMetricsNestingTests.cs:325-408`
- `tests/RoslynMcp.Tests/CompilationCacheTests.cs:83-246`

## Acceptance

- [ ] `DuplicateMethodDetectorTests`, `CodeMetricsNestingTests` and `CompilationCacheTests` delete their private workspace-manager fakes and construct `FailClosedWorkspaceManagerStub` with the wave-1 handlers. They keep their own workspace builders, because their subjects are not `UnusedCodeAnalyzer`.
- [ ] Each fake's behavior moves into a handler. The gate path in `DuplicateMethodDetectorTests` (`:900`) gets contains = true and stale = false. `CodeMetricsNestingTests` runs its `onGetCurrentSolution` callback inside the solution handler. `CompilationCacheTests` keeps a local version that its tests change (`:95`) and that the version handler reads.
- [ ] Assertions are unchanged, and each class keeps its test count and passes.

## Evidence

- At `716b5e20`: `DuplicateMethodDetectorTests.cs:1013-1053` is a byte-identical 41-line copy. `CodeMetricsNestingTests.cs:362-408` is the same copy plus an `onGetCurrentSolution` callback. `CompilationCacheTests.cs:211-246` (`FakeWorkspaceManager`) has a settable `Version` and a throwing `GetCurrentSolution`.
- Found by the PR #1665 review. The full recount of private workspace-manager fakes is in `server-probe-test-doubles-batch-1`.

## Context

- Wave 3 of 3. It depends on `unused-code-analyzer-test-harness-wave-1` for the stub handlers.
- `host-tools-code-quality-extraction` also anchors `DuplicateMethodDetectorTests.cs`. Sequence by hotspot if both are picked up.
