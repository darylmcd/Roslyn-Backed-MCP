# release-policy-requalify-parked-major-rows — Re-qualify rows still parked as major-only against Rule 1 / Rule 2

**row:** `release-policy-requalify-parked-major-rows` · **pri:** `Medium` · **size:** `S` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `ai_docs/backlog.md`

## Acceptance

Each citation below must be re-qualified against `docs/release-policy.md` § Corrections And Security Removals (Rule 1: corrections keeping response shape and error category ship in a 5.x minor; Rule 2: security removals of leaked caller input, paths or filter text ship in a minor with ADR + migration note), with before/after wire evidence, then either re-parked as major-only with that evidence or unparked. Line numbers are at origin/main 96629cae plus the two closes of this change.

- [ ] `ai_docs/backlog.md:76` `editorconfig-section-source-path-matching` (cites `release-policy.md:24-26`, ADR 0028); also `ai_docs/items/editorconfig-section-source-path-matching.md:132`.
- [ ] `ai_docs/backlog.md:95` `editorconfig-writer-effective-section-precedence` (major-only, v6.0.0).
- [ ] `ai_docs/items/editorconfig-reader-fidelity.md:24`.
- [ ] `ai_docs/items/editorconfig-writer-eol-normalization.md:13` (cites `release-policy.md:24-26`).
- [ ] `ai_docs/items/contract-breaking-fragment-adr-migration-ungated.md:19` (cites `release-policy.md:23-28,70`; line pins now stale, see `release-policy-line-pins-cite-heading`).
- [ ] `ai_docs/backlog.md:90` `workspace-close-global-build-server-shutdown` (BLOCKED: next major).
- [ ] `ai_docs/backlog.md:114` `code-action-preview-invalid-operation-classification`.
- [ ] `ai_docs/backlog.md:118` `test-run-global-json-fail-closed-authority`.
- [ ] `ai_docs/backlog.md:133` `extract-type-external-construction-sites`.
- [ ] `ai_docs/backlog.md:136` `extract-type-partial-overload-identity`.
- [ ] `ai_docs/backlog.md:148` `extract-type-protected-member-external-consumers`.
- [ ] `ai_docs/backlog.md:471` `document-and-type-not-found-report-invalid-operation`.
- [ ] `ai_docs/backlog.md:515` `type-extraction-bare-invalid-operation-refusals`.
- [ ] `ai_docs/backlog.md:529` `extract-type-partial-refusal-message-and-dup-loops`.
- [ ] `ai_docs/backlog.md:583` `interface-conflict-check-plain-ioe-first-project`.
- [ ] `ai_docs/backlog.md:615` `type-extraction-service-refusals-public-message` (Defer).
- [ ] `ai_docs/backlog.md:616` `refactoring-service-plain-caller-refusals` (Defer).
- [ ] `ai_docs/items/mutating-member-dto-flat-location-fields.md:12` (remove flat fields "at the next major").
- [ ] `ai_docs/items/not-found-metadata-only-message-classification.md:22`.
- [ ] `ai_docs/items/cross-project-type-name-first-match.md:15`.
- [ ] `ai_docs/items/rebase-changed-document-silent-drop.md:20-26` and `ai_docs/items/undo-reload-failure-reports-success.md:33-39` (Excluded 2026-11-10 "current-major-only" blocks; the 2026-11-20 unpark notes sit below them).
- [ ] Remove the stale "must first amend docs/release-policy.md:24-29" note, now satisfied by ADR 0041: `ai_docs/items/cross-project-public-refusals-echo-input.md:30`, `ai_docs/items/native-test-filter-public-projection.md:24`, `ai_docs/items/executed-log-line-leaks-filter-and-paths.md:28`; also reconcile the "ship in the next major" wording at `ai_docs/items/executed-log-line-leaks-filter-and-paths.md:14,26`.
- [ ] Re-run `rg -n "next major|major-only|release-policy.md:24" ai_docs/backlog.md ai_docs/items` after the edits; any remaining hit carries Rule 1 / Rule 2 evidence or is removed.

## Evidence

- PR 2168 merged `docs/release-policy.md` § Corrections And Security Removals and `docs/decisions/0041-stable-corrections-and-security-exception.md`; the citations above predate it and still assert that corrections and security removals need a major.
- The list is the output of `rg -n "next major|major-only|release-policy.md:2[4-9]" ai_docs/backlog.md ai_docs/items` at 96629cae, minus rows the 2026-11-20 operator decision already unparked.

## Context

- Premises are claims to re-derive: each row's old "major-only" call may be right (response shape or error category changes) even under the new rules.
