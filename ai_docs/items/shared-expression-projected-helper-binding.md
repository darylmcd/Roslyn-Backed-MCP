# shared-expression-projected-helper-binding — Verify bindings in the projected shared helper

**row:** `shared-expression-projected-helper-binding` · **pri:** `Medium` · **size:** `M` · **deps:** `shared-expression-match-bound-symbols`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs` (`BuildHelperMethod`, `RewriteHitsAndFormatDocumentsAsync`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`FindSimilarSymbols`, reference only)
- (new) `tests/RoslynMcp.Tests/ExtractSharedExpressionProjectedBindingTests.cs`

## Acceptance

- [ ] Through the experimental preview, prove two matching source sites can originally bind to the same local `Bind(int)` while the generated helper silently binds to a same-name type method. Refuse before token storage when the final projected helper cannot preserve that binding.
- [ ] Capture binding, implicit conversions, constant values and checked behavior in the exact helper body in the final preview projection. Cover an erased constant conversion that changes overload selection. Compare declaration/scope ownership and referenced symbols with the corresponding original operation. Preserve equivalent projected binding in reparsed preview and applied code.

## Evidence

- At `403de745`, `BuildHelperMethod` copies the example expression to a static type member and converts free variables to parameters. Comparing original candidate bindings cannot verify bindings after this change of compilation context.
- The cold plan probe resolved `const int n = 0; Bind(n)` to `Bind(byte)`. With a synthesized `int n` parameter it resolved the same source text to `Bind(long)`. Both versions compile. The prerequisite matcher should supply declaration and anonymous-function mappings that the projected comparison can reuse.
