# file-create-and-scaffold-refusals-public-message — Return file-creation and first-test scaffold refusals verbatim

**row:** `file-create-and-scaffold-refusals-public-message` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:37`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs`
- `tests/RoslynMcp.Tests/FileOperationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] create_file_preview / move_file_preview onto an existing file state that the file already exists, naming it relative to the workspace (FileOperationService.cs:37, :143) — no absolute path.
- [ ] scaffold_first_test_file_preview refusals state their reason instead of "Check the tool contract and retry."
- [ ] Regression tests assert both public messages through the tool error envelope.

## Evidence

- create_file_preview (existing file) and scaffold_first_test_file_preview refusals returned the generic InvalidOperation fallback — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Uses the existing `PublicInvalidOperationException`.
