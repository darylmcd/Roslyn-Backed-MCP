# shared-expression-unsafe-use-sites-rely-on-compile-check — Shared-expression special use sites have no explicit guard or test

**row:** `shared-expression-unsafe-use-sites-rely-on-compile-check` · **pri:** `Low` · **size:** `M` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:690`
- `src/RoslynMcp.Roslyn/Helpers/SharedExpressionProjectionVerifier.cs`

## Acceptance

- [ ] Per site (`?.` receiver, foreach ref iteration, `fixed` initializer, stackalloc): a test shows the preview is refused (or the rewrite is valid and compiles); gaps get an explicit refusal.

## Evidence

- shared-expr report bad-code sketch (PR 2142): these use sites rely only on the compile-error check, not a syntactic guard.
