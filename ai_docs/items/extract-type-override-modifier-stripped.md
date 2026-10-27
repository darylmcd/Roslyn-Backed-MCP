# extract-type-override-modifier-stripped — Investigate extract_type dropping `override`/inheritance modifiers

**row:** `extract-type-override-modifier-stripped` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1148-1160`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Determine whether extracting an `override` member silently discards the base-class contract (the extracted sealed class has no base); either refuse with a structured blocking dependency or document the intended behavior with a test.

## Evidence

- Deepener for `extract-type-interface-implementation-guard` (backlog-remediate 20261001T130338Z) reported `StripInheritanceOnlyModifiers` drops `override`. HEAD shows `private static MemberDeclarationSyntax StripInheritanceOnlyModifiers(MemberDeclarationSyntax member)` at `:1148` applied to every extracted member at `:581`; `BuildStrippedModifierList` (not opened this session) decides what is dropped. Unverified beyond that: investigate first.
- 2026-10-27 runtime probe: extracting `public override string ToString() => "O1"` leaves the helper hiding `object.ToString()` (CS0114) and `new O1(h).ToString()` returns `X.O1` instead of `O1`: a silent dispatch change.
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs:492-549` asserts a successful preview that strips `override` from an abstract-member implementation (`Down`), leaving the source missing the abstract implementation (CS0534). The test enshrines the defect: invert it and refuse moving any `override` member with a structured blocking dependency, as `TypeExtractionService.cs:1029-1073` already does for interface implementations.

## Context

- Different mechanism from the interface guard and private-field fixes; filed investigate-first per Directive #3.
