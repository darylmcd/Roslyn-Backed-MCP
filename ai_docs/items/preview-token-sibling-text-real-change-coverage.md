# preview-token-sibling-text-real-change-coverage — Assert real sibling text changes

**row:** `preview-token-sibling-text-real-change-coverage` · **pri:** `Medium` · **size:** `S` · **deps:** `watcher-own-cs-write-echo-attribution`

## Anchors

- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs:111`

## Acceptance

- [ ] Replace the existing sibling text preservation fixture's no-op-capable organize-usings pair with two real different text mutations through public preview/apply.
- [ ] Assert both exact contents in disk and current workspace after both redemptions; file existence alone is insufficient.
- [ ] Remove stale skip commentary and prove the regression rejects an implementation that drops sibling content.
- [ ] Obtain fresh independent review, required gates and verified merge before closure.

## Evidence

- 2026-10-23 cold rebase review: the existing Sibling_Text_Edit_Apply_Does_Not_Invalidate_Other_Text_Edit_Token test admits no-op organize-usings and asserts only file existence. New reference-focused tests do not repair that separate text-only contract.

## Notes

Red on unchanged production at 573540d5: host format_document_preview issued two real edits, first apply wrote expected bytes, second format_document_apply returned a stale-token result. Focused fixture plus related tests: 1 failed, 35 passed. WorkspaceManager increments on apply and reload; the default auto-reload may exceed the pinned preview span. Reproduce both raw eras and qualify owned-write attribution (watcher-own-cs-write-echo-attribution), version, stale metadata, genuine external reloads, conflicts and token eviction. If the complete fix requires changing the released contract, resume at the next operator-approved major with ADR and migration. Fixture and logs were retained in the run scratchpad.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
