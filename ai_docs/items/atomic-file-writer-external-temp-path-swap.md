# atomic-file-writer-external-temp-path-swap — Prevent adversarial replacement of atomic-write temp paths

**row:** `atomic-file-writer-external-temp-path-swap` · **pri:** `Defer` · **size:** `M` · **deps:** `editorconfig-write-no-workspace-invalidation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Commit the retained temporary file to the destination without a pathname check/use gap that permits another process with directory write access to substitute a different file after validation.
- [ ] Preserve same-directory atomic replacement, cancellation, and cleanup ownership across Windows and Linux.
- [ ] Regress an external temp-path substitution in the interval immediately before commit; prove the foreign bytes or link cannot become the destination.

## Evidence

- PR #1678 makes each atomic write use a unique, held temporary file and rejects substitutions detected before replacement. `File.Move(tempPath, targetPath)` still resolves `tempPath` after the identity check. A noncooperating external process with write access to the directory can replace that pathname in the gap. The former fixed `.tmp` helper had the same path-based race; the new helper closes honest concurrent-write collisions and detectable substitutions, not this adversarial interval.

- 2026-10-23: semantic references found no production consumer of the retired AtomicFileWriter. Current uncommitted source removes it and its 16 helper-only test methods, preserving actual Composite/undo tests and native parser-handle overwrite proof. Runtime qualification and merge remain pending; row remains open.
