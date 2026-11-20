# document-set-native-default-compile-policy — Preserve native Compile membership during source apply

**row:** `document-set-native-default-compile-policy` · **pri:** `Medium` · **size:** `L` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:405` — raw XML default Compile classifier through459.
- `src/RoslynMcp.Roslyn/Services/ProjectReferenceMutationRenderer.cs:71` — policy decision for physical document-set rendering.
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:198` — snapshot policy; native apply283 and project reconciliation287–301.
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs:117` — common physical-rendering preview seam.
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:95` — existing native evaluator construction/factory reuse boundary; do not add another graph walker.
- `tests/RoslynMcp.Tests/SdkStyleCsprojInjectionTests.cs:32` — ordinary default SDK original-byte controls.

## Acceptance

- [ ] Re-derive and split this investigation into independently correct dependency-chained implementation rows before writing code; the preserved v3 plan is unapproved reference material.
- [ ] Observe causal RED for public add/remove/sibling operations whose actual Compile membership differs under EnableDefaultItems, EnableDefaultCompileItems, imported/conditional values or later overrides; preserve successful controls and distinguish undo failures from policy failures.
- [ ] Make intended physical source effects compile exactly once, or remain absent after removal, under actual applicable native contexts. Preserve ordinary default SDK byte equality, explicit disabled-item inclusion, conditional item ownership and unrelated metadata.
- [ ] Keep preview, source apply, rollback, physical undo and source/token authority consistent. Do not suppress native disagreement, weaken guards or import BuildHost/certificate/lease machinery.
- [ ] Complete fresh review, serialized affected producer and hosted qualification; do not treat scratch plan or partial cohorts as implementation approval.

## Evidence

- Main58ba4207 raw XML classifier omits effective EnableDefaultItems, imported policy, conditions and overrides; renderer and persistence share that classifier.
- Preserved wrapped OLD/current causal corpus:24 total,18 failed/6 passed; source checkpoint distinguishes15 policy and3 undo failures. Do not count all18 as this mechanism or claim post1828 qualification.
- Receipts `e2-native-default-policy-old-causal.log` and `e2-native-default-policy-current-causal.log`, fixtures/frozen tests and `native-default-policy-plan-v3.md` are under `<user>/.codex/scratch/roslyn-remediate-20261023-split/`.
- 2026-10-27: the preserved PR1826 test edits are inlined in this row; their archive branch was deleted (PR1826 head stays at GitHub `refs/pull/1826/head`).
- 2026-10-27 test matrix inlined from closed PR1826: entry project in 3 SDK shapes (`Sdk` attribute, `<Sdk>` element, explicit `Sdk.props`/`Sdk.targets` imports) × property `EnableDefaultItems` or `EnableDefaultCompileItems` × scenario (literal false; false imported from `Defaults.props`; false under an inactive `Condition`; false then a later true). Steps: `create_file` `Added.cs`, apply. Assert: apply succeeds; exactly 1 evaluated `Compile` item for `Added.cs` under workspace globals; undo leaves 0 items and every original byte; the classifier equals native `EnableDefaultItems && EnableDefaultCompileItems`.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:469-485` scans literal `EnableDefaultCompileItems` elements in the root file only: it misses `EnableDefaultItems`, imports and conditions, and returns false on the first `false` even when a later `true` overrides it. Its "user is taking responsibility" comment is a stale rationale.

## Context

- Independent nonblocking source-policy remainder; restore-callers-missing-packages-path closure does not depend on it.
- Size L records five known shared investigation seams and unresolved integration, not a mandate to edit each file or adopt the prior design.

## Notes

2026-11-17 screen at c705e2e1. Release impact unqualified. ProjectReferenceMutationRenderer.cs:71 and DocumentSetPersistenceService.cs:197 share the raw XML Compile policy. Candidate changes can affect stable create/delete/move preview output and preview-to-apply project edits. Qualify the complete preview, apply, rollback and undo behavior against the 5.x wire and file effects before choosing a slice.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
