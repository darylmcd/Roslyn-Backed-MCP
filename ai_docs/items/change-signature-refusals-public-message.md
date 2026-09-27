# change-signature-refusals-public-message — Return change_signature and format_range refusals verbatim

**row:** `change-signature-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `format-range-refuses-on-unrelated-line-count-change`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:86`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1199`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] change_signature_preview reorder/validation refusals (ChangeSignatureService.cs:86, :332, :476) state their reason instead of "Check the tool contract and retry."
- [ ] Any format_range_preview refusal still present after `format-range-refuses-on-unrelated-line-count-change` lands states its reason (RefactoringService.cs:1199).
- [ ] Regression test asserts one change_signature refusal's public message through the tool error envelope.

## Evidence

- format_range_preview refusal and change_signature reorder errors returned the generic InvalidOperation fallback — mcp-surface-audit 20260924-1305 (check C1).

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26). Depends on `format-range-refuses-on-unrelated-line-count-change`, which may remove the format_range refusal entirely.
