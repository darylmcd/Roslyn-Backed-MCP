# type-move-nested-and-ambiguous-selection

**row:** `type-move-nested-and-ambiguous-selection` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Use semantic identity or fail with safe guidance when names collide or the selected declaration is nested. Pin a nested/simple-name collision regression without issuing a token that changes type identity.

## Evidence

DescendantNodes plus FirstOrDefault selects the first simple-name match, including nested declarations; StripInvalidTopLevelModifiers then changes visibility and ownership. Verified during the 2026-09-14 tool-contract review.
