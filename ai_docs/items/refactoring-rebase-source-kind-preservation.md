# refactoring-rebase-source-kind-preservation

**row:** `refactoring-rebase-source-kind-preservation` · **pri:** `High` · **size:** `M`

## Anchors
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:858`
- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs:812`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:165`
- `src/RoslynMcp.Roslyn/Services/RestoredCompilerInputAuthority.cs:589`
- (new) `tests/RoslynMcp.Tests/DocumentSourceKindReplayTests.cs`
- (new) `tests/RoslynMcp.Tests/NativeDocumentMoveStateTests.cs`

## Acceptance
- [ ] Preserve exact reviewed source kind in same-coordinate and reconstruction rebase branches plus full existing-document move/native refresh clones.
- [ ] Compare exact source kind during expected/native document equivalence before text; refuse unsupported representations before effects. Preserve graph-only comparison behavior.
- [ ] Observe deterministic sibling-rebase and comparator regression tests fail on old code and pass after fix; retain unrelated sibling edits, relocation, additions and Script preservation/reset controls.
- [ ] Qualify actual native move and actual .csx kind before certification verdict; do not forge Adhoc authority or claim unobserved native Script support.
- [ ] Preserve all existing native compiler/refusal checks and probe same clone mechanism; generated new artifacts do not blindly inherit unrelated whole-file Script identity.

## Evidence
- Parent and independent readers rederived kind intent detection, two rebase losses, physical comparator omission and full-document clone sites.
- Cold plan reviewed four production files and thirteen semantic cases; new native four-case fixture corrections under final cold source reread.
- Scratch sourcekind-handoff.md records fourteen AddDocument sites and distinctions; source25 remains frozen, tests-first old native proof pending, no production patch applied or pass claimed.
