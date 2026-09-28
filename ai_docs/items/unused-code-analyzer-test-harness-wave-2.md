# unused-code-analyzer-test-harness-wave-2 — Retire the private workspace-manager copies in the remaining unused-code analyzer suites (wave 2 of 3)

**row:** `unused-code-analyzer-test-harness-wave-2` · **pri:** `Low` · **size:** `S` · **deps:** `unused-code-analyzer-test-harness-wave-1`

## Anchors

- `tests/RoslynMcp.Tests/DeadLocalDetectorTests.cs:613-699`
- `tests/RoslynMcp.Tests/DuplicateHelperDetectionTests.cs:463-611`
- `tests/RoslynMcp.Tests/UnusedSymbolScanFailSafeTests.cs:197-307`

## Acceptance

- [ ] `DeadLocalDetectorTests`, `DuplicateHelperDetectionTests` and `UnusedSymbolScanFailSafeTests` delete their private `TestWorkspaceManager` and build their analyzer through the wave-1 harness over `FailClosedWorkspaceManagerStub`.
- [ ] Caller-specific inputs survive. Dead-local keeps its extra BCL references (`Console`, `MemoryStream`). Duplicate-helper keeps its additional metadata references and the `CreateAdhocWorkspace` reuse in `GetCompilationErrorsAsync`. The fail-safe suite keeps its `referenceFinder` / `exceptionReporter` inputs and the manager and `PreviewStore` it passes to `DeadCodeService` (`:150`).
- [ ] When a suite needs an input the harness lacks, the harness grows it; no suite keeps a private copy. The "duplicated rather than shared" comment at `DeadLocalDetectorTests.cs:654-658` is deleted with the copy.
- [ ] Assertions are unchanged, and each class keeps its test count and passes.

## Evidence

- At `716b5e20`: byte-identical 41-line `TestWorkspaceManager` copies at `DeadLocalDetectorTests.cs:659-699`, `DuplicateHelperDetectionTests.cs:571-611` and `UnusedSymbolScanFailSafeTests.cs:267-307`. The analyzer builders are at `DeadLocalDetectorTests.cs:613-652`, `DuplicateHelperDetectionTests.cs:463-528` and `UnusedSymbolScanFailSafeTests.cs:197-249`.
- Found by the PR #1665 review. The full recount of private workspace-manager fakes is in `server-probe-test-doubles-batch-1`.

## Context

- Wave 2 of 3. It depends on `unused-code-analyzer-test-harness-wave-1` for the harness and the stub handlers.
- `unused-code-analyzer-dead-local-complexity` and `unused-code-analyzer-duplicate-helper-complexity` also anchor `DeadLocalDetectorTests.cs` and `DuplicateHelperDetectionTests.cs`. Sequence by hotspot if they are picked up together.
