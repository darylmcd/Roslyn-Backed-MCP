# shared-expression-capture-lexical-boundary — Recognize variables declared inside selected shared expressions

**row:** `shared-expression-capture-lexical-boundary` · **pri:** `Medium` · **size:** `S` · **deps:** `shared-expression-preserve-capture-read-timing`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:941`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Use a selected expression with a lambda-owned parameter and a distinct real outer capture. Prove only the outer capture becomes a helper argument, then verify preview and applied compilation and binding. Keep equivalent independently scoped declarations and safely invoked lambdas eligible.
- [ ] Treat a selected value inside an already-enclosing implicit query lambda as a selection within that lambda. Refuse an inline declaration that still has bound uses outside the selected expression. Exercise both the example and a structurally matching candidate.
- [ ] Preserve the bound compile-time result of `nameof` without reading an unassigned outer variable at runtime.
- [ ] With `Pair(v => v + 1, update(), number)` and an earlier argument that mutates `number`, preserve the original result 2 or refuse before token storage. Reuse the capture-read timing prerequisite.

## Evidence

- CollectFreeVariables says it excludes symbols declared inside the expression. The walk accepts local and parameter symbol kinds without checking their declaring scope. A selected invocation containing a lambda can pass its parameter as an out-of-scope argument.

## Notes

The exact 403de745 base generated an invalid reference to an inner lambda parameter. Cold review and a native preview/apply probe found that the lexical candidate built from that base generated compiling code and changed the lambda variant result from 2 to 1. The old collector had rejected or invalidated similar changes whose nested bindings differ between sites. Land the candidate-binding, projected-helper binding and eager-read timing prerequisites. Then require exact selected-operation ownership for already-enclosing query lambdas and preserve compile-time nameof without reading an unassigned variable. Keep safe immediately invoked lambdas and equivalent selection-owned declarations covered. This remains experimental 5.x work.
