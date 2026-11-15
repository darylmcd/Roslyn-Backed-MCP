# shared-expression-match-bound-symbols — Preserve expression operand bindings during shared extraction

**row:** `shared-expression-match-bound-symbols` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:724`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Compare bound operations for matching shared-expression candidates. Allow corresponding free variables while requiring equivalent non-capture symbols and operations.
- [ ] Prove red first through the public preview using identical syntax that binds to distinct enclosing members. Prevent a compiling rewrite that changes results and retain successful equivalent-binding preview/apply.

## Evidence

- At 9e7a9439, the matcher uses syntax equivalence and compares capture counts and types. Two expressions spelled `GetValue(x)` can call different enclosing methods, and a common helper selects the example's binding for both.
- Cold plan review identified this as a separate mechanism from binding the generated helper qualifier.
