# extract-type-modifier-trivia-loss — Preserve modifier comments during type extraction

**row:** `extract-type-modifier-trivia-loss` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (`BuildStrippedModifierList`, `EnsurePublicAccessibility`)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Removing `new`, `virtual`, or an access modifier preserves comments and other non-whitespace trivia attached to the removed token, including trivia between modifiers.
- [ ] Red-first extraction regressions cover comments before, between, and after removed modifiers and verify the emitted declaration retains them in a stable order.
- [ ] Existing type extraction behavior and tests remain intact.

## Evidence

- Cold review of PR #1951 at head `8e90203afea631d26a63a3045a88bc393868ae29`: `BuildStrippedModifierList` keeps only the original first token's leading trivia when dropping a modifier; `EnsurePublicAccessibility` filters access tokens without moving their trivia. Comments on removed tokens can disappear. This behavior predates that PR and is separate from its member-kind dispatch fix.
