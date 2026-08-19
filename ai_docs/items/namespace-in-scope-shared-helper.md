# namespace-in-scope-shared-helper — one namespace-in-scope test across rewrite services

**row:** `namespace-in-scope-shared-helper` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1201`
- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:117`

## Acceptance

- [ ] A single helper decides whether a namespace is in scope at a syntax position — the enclosing-namespace chain plus non-alias, non-static usings on the compilation unit AND on enclosing namespace declarations.
- [ ] `ParameterObjectService` and `BulkRefactoringService` both call it; the weaker compilation-unit-only check in `BulkRefactoringService` is deleted, with a test covering a namespace-level using.

## Evidence

Both sites read: `ParameterObjectService.cs:1222-1240` walks `AncestorsAndSelf` collecting `CompilationUnit` / `BaseNamespaceDeclaration` usings and filters alias/static directives; `BulkRefactoringService.cs:117` does the same job with only `compilationUnit.Usings` and no alias/static filter, so it reports false for a namespace-level or aliased using.

## Context

Low-severity duplication finding from PR #1271 (`parameter-object-dto-reference-qualification`) code-quality review. Advisory — did not block. Note the two copies are not equivalent: the older one is strictly weaker, so this is a latent correctness gap in `BulkRefactoringService`, not pure tidy-up.
