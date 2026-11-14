# composite-preview-complete-review-remaining-producers — Refuse redeemable incomplete composite reviews

**row:** `composite-preview-complete-review-remaining-producers` · **pri:** `Medium` · **size:** `M` · **verified:** `573540d5b8a720c92c479993804045c78c75676c` · **deps:** `composite-batch-final-diff-composition`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:92-97` — source and partial-file diffs.
- `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs:81` — stores mutations after capped diffs at 164 and 194.
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:212` — stores the service split previews rendered at 414, 483 and 520.
- `src/RoslynMcp.Roslyn/Services/CompositeSolutionMutationPlanner.cs` — shared complete-review renderer from the prerequisite.

## Acceptance

- [ ] Route each of the three producers through the shared complete physical-delta renderer. Preserve each producer's exact original and replacement content, path modes, intent and existing operation limits.
- [ ] Reproduce an oversized file and an aggregate cap across distinct files. Every affected producer refuses before storing a redeemable token when the review cannot describe all writes.
- [ ] Prove ordinary previews across all three producers retain exact reviewed bytes and existing apply behavior. Verify advertised tiers before implementation.

## Evidence

- 573540d5: each route emits a string-only bounded DiffGenerator result and issues an apply token from full-content mutations. The composite store has no authority to redeem a truncated review. Record-satellite previews use structured insertion edits and bypass the capped renderer.
- Required completion slice from independent cold planning of composite-batch-final-diff-composition. Keep it separate from the global PreviewStore provenance work, which uses a different store.
