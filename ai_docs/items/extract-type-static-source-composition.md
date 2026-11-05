# extract-type-static-source-composition — Skip instance composition for static-only moves

**row:** `extract-type-static-source-composition` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:132-134`, `:743-755` — field and constructor injected unconditionally.

## Acceptance

- [ ] Red first: `public static class S1 { static int Compute() => 7; static int Use() => Compute(); }`, extract `Compute`: current output fails CS0708/CS0710 (instance field and constructor in a static class).
- [ ] All-static moves leave the source constructors and fields untouched and rewrite callers to `Helper.Member`; mixed moves are unchanged.
- [ ] Pure-static moves from instance classes no longer add a required constructor parameter.

## Evidence

- 2026-10-27: CS0708 and CS0710 observed on main's rendered output.

## Context

- Prerequisite for `extract-type-value-type-and-record-composition` and `extract-type-external-construction-sites`.

## Notes

Re-verified 2026-11-05 at f15e28e7: raised to High because it blocks High row extract-type-value-type-and-record-composition (collapse-first, TypeExtractionService.cs). Anchors drifted: the unconditional call is at :145-147 and InjectFieldAndCtorParameter at :1432-1580. Today the example is REFUSED, not mis-rendered: RefuseAddedCompilerErrorsAsync (:166) throws a PublicInvalidOperationException naming CS0708/CS0710, pinned by the StaticHostFixture DataRow at TypeExtractionTests.cs:1635, which must be inverted. Fix: inject the composition field only when some moved symbol is non-static; keep the partial-type refusal at :51-57.
