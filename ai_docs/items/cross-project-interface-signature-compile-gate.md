# cross-project-interface-signature-compile-gate — Check generated interface compilation

**row:** `cross-project-interface-signature-compile-gate` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-signature-refusal-compile`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:183`
- `tests/RoslynMcp.Tests/TestInfrastructure/InterfaceExtractionTestContext.cs:75`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:485`

## Acceptance

- [ ] A generated interface with new compiler errors in the pending target or affected projects refuses before preview token storage; direct extraction, dependency inversion and extract-and-wire tests assert compilation strictly, including source-owned cycle cases.

## Evidence

- At `0121d9ab`, extraction stores previews without target compilation, and AssertNoUnresolvedNamesAsync tolerates member-mismatch diagnostics. Cold review found strict fixtures cannot precede the production closure and cycle fixes.

## Context

- Final slice of cross-project-interface-signature-dependencies. Migrate tolerant success assertions while retaining explicit source-owned-cycle refusal tests. Preserve existing unrelated diagnostics by comparing baseline and pending compilations.
