# shared-expression-preserve-capture-read-timing — Preserve capture read timing during shared extraction

**row:** `shared-expression-preserve-capture-read-timing` · **pri:** `Medium` · **size:** `S` · **deps:** `shared-expression-projected-helper-binding`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs` (`CollectFreeVariables`, `BuildInvocation`)
- (new) `tests/RoslynMcp.Tests/ExtractSharedExpressionCaptureTimingTests.cs`

## Acceptance

- [ ] Prove red through experimental preview and verified apply. An earlier `update()` mutates an outer `number` through a closure. Extracting `Pair(update(), number)` must preserve the result or refuse before storing a token.
- [ ] Account for evaluation order and potential writes through captured, aliased and by-reference storage at each replacement. Preserve safe eager reads, including reads before an effect, and prove accepted preview and applied results.

## Evidence

- Native verification against an exact `403de745` archive executed 1 originally, 0 from the compiling preview, and 0 from reparsed applied source. `BuildInvocation` reads all outer captures before evaluating the moved expression.
- The in-progress lexical fix enabled `Pair(v => v + 1, update(), number)`. Its native proof executed 2 originally and 1 in both preview and applied source. The baseline generated an invalid reference to `v`, so the wrong result becomes newly reachable with lexical-boundary work.
