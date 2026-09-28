# change-signature-refusals-public-message — Return change_signature and format_range refusals verbatim

**row:** `change-signature-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `format-range-refuses-on-unrelated-line-count-change, public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`
- `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs`

## Acceptance

- [ ] Existing acceptance bullets kept.
- [ ] change_signature_preview op='reorder' with a duplicate token names the parameter through the envelope.
- [ ] Range refusals name startLine/endLine and the file's line count.

## Evidence

- format_range_preview refusal and change_signature reorder errors returned the generic InvalidOperation fallback — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Depends on `format-range-refuses-on-unrelated-line-count-change`, which may remove the format_range refusal entirely.
2026-09-26: re-scoped as a child of the argument-error contract redesign.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
