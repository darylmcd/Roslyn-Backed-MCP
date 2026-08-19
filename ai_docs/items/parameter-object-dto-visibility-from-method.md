# parameter-object-dto-visibility-from-method — derive DTO visibility from the method, not just the containing type

**row:** `parameter-object-dto-visibility-from-method` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:709`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1030`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:699`

## Acceptance

- [ ] Same-project DTO visibility is the narrowest legal option across containing type, method accessibility, and grouped parameter types; the accessibility gate refuses only when NO legal visibility exists.
- [ ] An `internal` method on a `public` class grouping an `internal` parameter type previews an `internal` record instead of refusing, with a regression test.

## Evidence

`ResolveDtoProject` (`:730`) sets `VisibilityIsPublic` from `method.ContainingType.DeclaredAccessibility`, and the gate added by PR #1269 compares parameter types against that rank. The shipped DataRow "internal type into public record" (an `internal` method on a `public` class) therefore refuses a case an `internal` record would compile cleanly.

## Context

**Over-refusal shipped knowingly in PR #1269** (`parameter-object-generic-dto-type-validity`) — the passing test asserts the refusal, so this is encoded behaviour, not an accident. Surfaced as a medium `anti-pattern` finding by that PR's code-quality review; advisory, so it did not block the merge. Fixing it means changing that DataRow's expectation.
