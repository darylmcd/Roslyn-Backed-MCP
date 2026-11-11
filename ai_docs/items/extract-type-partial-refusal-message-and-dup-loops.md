# extract-type-partial-refusal-message-and-dup-loops — Reword the stale partial-declaration refusal message

**row:** `extract-type-partial-refusal-message-and-dup-loops` · **pri:** `Low` · **size:** `S` · **verified:** `95f651345b37a2849fe02bd026f827813658573f` · **deps:** `type-extraction-bare-invalid-operation-refusals`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:51-57` — multi-part refusal cites composition-field wiring but also fires for static-only moves (no field is wired).
- `tests/RoslynMcp.Tests/InvalidOperationConstructionRatchetTests.cs` — fingerprints plain `new InvalidOperationException` constructions.

## Acceptance

- [x] Shipped (PR #2066, row `type-extraction-extracted-symbol-set-duplication`): the duplicated extracted-symbol loop is consolidated.
- [ ] Reword the partial refusal so it is accurate for static-only moves. The construction is a plain `new InvalidOperationException` fingerprinted by InvalidOperationConstructionRatchetTests: a reword is a new identity and baseline edits are forbidden, so the only compliant route converts it to PublicInvalidOperationException = public message change (major).

## Evidence

- 2026-11-11 re-derivation at 95f65134: message at TypeExtractionService.cs:51-57 unchanged; the duplicated-loop half shipped.
