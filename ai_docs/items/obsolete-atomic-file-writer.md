# obsolete-atomic-file-writer — Remove the obsolete competing atomic file writer

**row:** `obsolete-atomic-file-writer` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Recheck semantic references; remove the writer only after confirming zero production consumers.
- [ ] Remove helper-only tests, migrate owned fixture setup and retain all live physical-transition regression controls.
- [ ] Leave one canonical physical source-write/recovery mechanism; no unused compatibility wrapper.
- [ ] Run meaningful current composite/editor-config/source-transition tests.

## Evidence

- Independent full-diff source review rederived this defect in the current retained restore implementation. Required fix in this PR; this row does not authorize deferral.

- 2026-10-23: semantic references found no production consumer of the retired AtomicFileWriter. Current uncommitted source removes it and its 16 helper-only test methods, preserving actual Composite/undo tests and native parser-handle overwrite proof. Runtime qualification and merge remain pending; row remains open.
