# type-extraction-stale-ctor-assignment-doc-comment — Doc comment says the assignment is appended; it is inserted first

**row:** `type-extraction-stale-ctor-assignment-doc-comment` · **pri:** `Low` · **size:** `S` · **verified:** `95f651345b37a2849fe02bd026f827813658573f`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1282` — "the assignment is appended".
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1441` — `ctor.Body.Statements.Insert(0, assignment)`.

## Acceptance

- [ ] The doc comment states the assignment is inserted as the first statement of the constructor body (and why it matters, if the reason is still true: constructor bodies no longer need refusal).
- [ ] Sweep the rest of the method summary for the same stale claim.

## Evidence

- 2026-11-11 re-derivation at 95f65134: comment at :1282 versus insertion at :1441.
