# extract-type-static-source-composition — Skip instance composition for static-only moves

**row:** `extract-type-static-source-composition` · **pri:** `Medium` · **size:** `S`

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
