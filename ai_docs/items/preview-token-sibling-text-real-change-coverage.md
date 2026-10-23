# preview-token-sibling-text-real-change-coverage — Assert real sibling text changes

**row:** `preview-token-sibling-text-real-change-coverage` · **pri:** `Medium` · **size:** `S` · **deps:** `preview-project-reference-metadata-rebase`

## Anchors

- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs:111`

## Acceptance

- [ ] Replace the existing sibling text preservation fixture's no-op-capable organize-usings pair with two real different text mutations through public preview/apply.
- [ ] Assert both exact contents in disk and current workspace after both redemptions; file existence alone is insufficient.
- [ ] Remove stale skip commentary and prove the regression rejects an implementation that drops sibling content.
- [ ] Obtain fresh independent review, required gates and verified merge before closure.

## Evidence

- 2026-10-23 cold rebase review: the existing Sibling_Text_Edit_Apply_Does_Not_Invalidate_Other_Text_Edit_Token test admits no-op organize-usings and asserts only file existence. New reference-focused tests do not repair that separate text-only contract.
