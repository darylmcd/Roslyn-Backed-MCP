# undo-capture-call-site-cleanup — Collapse repeated capture/record call-site boilerplate

**row:** `undo-capture-call-site-cleanup` · **pri:** `Low` · **size:** `L` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs:103` — capture site (also :164, and ApplyTextEditsCoreAsync :695 taking both suppressChangeTrackerRecord and a capture id; batch passes None with suppress=true).
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:350`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:127`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:432` — also :425.
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:573`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs:579`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs:839` — also :979.

## Acceptance

- [ ] One helper replaces the repeated `_undoService?.CaptureBeforeApply(...) ?? UndoCaptureId.None` sites.
- [ ] Record is split out of ApplyTextEditsCoreAsync, or the redundant suppress flag is removed.
- [ ] Shared test helper replaces hand-driven capture+record pairs. No behavior change; existing tests pass.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): PR #2057 threaded capture ids through every call site, leaving the duplicated shape.

## Notes

### Context

Size L: the five production call sites must migrate to the shared helper in one change so no site is left half-migrated; behavior is unchanged.
