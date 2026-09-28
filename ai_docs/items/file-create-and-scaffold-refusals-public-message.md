# file-create-and-scaffold-refusals-public-message — Return file-creation and first-test scaffold refusals verbatim

**row:** `file-create-and-scaffold-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs`
- `tests/RoslynMcp.Tests/FileOperationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] Existing acceptance bullets kept.
- [ ] move_file_preview with identical source and destination names the parameter; the outside-project refusal names paths relative to the project.

## Evidence

- create_file_preview (existing file) and scaffold_first_test_file_preview refusals returned the generic InvalidOperation fallback — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Uses the existing `PublicInvalidOperationException`.
2026-09-26: re-scoped as a child of the argument-error contract redesign.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.

## Notes

- 2026-09-28: mechanism parent `tool-refusal-public-message-guard` adds a ratchet that fails any new plain `InvalidOperationException` under `src/`; this row's InvalidOperationException half is part of its burn-down. Convert those sites to `PublicInvalidOperationException` (or the Core internal factory for true invariants) and lower the committed baseline count in the same PR when the ratchet has landed first.
