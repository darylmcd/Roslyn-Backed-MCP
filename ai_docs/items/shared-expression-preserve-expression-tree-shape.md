# shared-expression-preserve-expression-tree-shape — Preserve caller expression-tree structure in shared extraction

**row:** `shared-expression-preserve-expression-tree-shape` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs` (`CollectMatchingExpressionHitsAsync`, `BuildInvocation`)
- (new) `tests/RoslynMcp.Tests/ExtractSharedExpressionValueContextTests.cs`

## Acceptance

- [ ] Through the experimental preview, show that rewriting repeated `BindingTarget.GetValue(7)` selections inside `Expression<Func<int>>` lambdas changes the expression tree seen by a provider. Preserve the original tree or refuse before storing a token.
- [ ] Preserve matching delegate contexts where the helper rewrite does preserve behavior. Exercise mixed delegate and expression-tree occurrences with each context as the example, and validate accepted preview and apply.

## Evidence

- Source inspection at `403de745`. Two originals can bind identically and the helper body can preserve invocation binding while the enclosing tree now exposes a call to the generated helper. Provider dispatch sees the rewritten call target before it could execute the helper.
