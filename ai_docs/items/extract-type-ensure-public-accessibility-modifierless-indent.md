# extract-type-ensure-public-accessibility-modifierless-indent — Modifier-less member widening displaces indentation

**row:** `extract-type-ensure-public-accessibility-modifierless-indent` · **pri:** `Low` · **size:** `S` · **verified:** `95f651345b37a2849fe02bd026f827813658573f`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1832-1859` — EnsurePublicAccessibility; the `currentModifiers.Count == 0` branch gives the inserted token only a trailing space, so the member's own leading trivia stays on the next token.
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs:111` — pins today's output `public     int Plain;` as existing behavior (PR #2066).

## Acceptance

- [ ] Red first: widening `int Plain;` yields `    public int Plain;` (leading indent on `public`, single space after); update the pinned DataRow at :111.
- [ ] Probe a leading `//` comment on a modifier-less member: the next token must keep its indentation. If the defect does not reproduce, record that in the row and drop the clause.
- [ ] Same probe on the StripInheritanceOnlyModifiers path; fix any same-mechanism instance.

## Evidence

- PR #2066 tests pin the current output; the pin was accepted because the fix changes extracted-code formatting for existing users.
