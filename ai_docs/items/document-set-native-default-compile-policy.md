# document-set-native-default-compile-policy — Preserve native Compile membership during source apply

**row:** `document-set-native-default-compile-policy` · **pri:** `Medium` · **size:** `L`

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
- PR1826 branch `codex/restore-fork-reviewed-project-graph` preserves related WIP; no code is authorized for copying across.

## Context

- Independent nonblocking source-policy remainder; restore-callers-missing-packages-path closure does not depend on it.
- Size L records five known shared investigation seams and unresolved integration, not a mandate to edit each file or adopt the prior design.
