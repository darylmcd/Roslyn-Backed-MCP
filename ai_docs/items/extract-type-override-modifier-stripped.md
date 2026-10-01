# extract-type-override-modifier-stripped — Investigate extract_type dropping `override`/inheritance modifiers

**row:** `extract-type-override-modifier-stripped` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1148-1160`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Determine whether extracting an `override` member silently discards the base-class contract (the extracted sealed class has no base); either refuse with a structured blocking dependency or document the intended behavior with a test.

## Evidence

- Deepener for `extract-type-interface-implementation-guard` (backlog-remediate 20261001T130338Z) reported `StripInheritanceOnlyModifiers` drops `override`. HEAD shows `private static MemberDeclarationSyntax StripInheritanceOnlyModifiers(MemberDeclarationSyntax member)` at `:1148` applied to every extracted member at `:581`; `BuildStrippedModifierList` (not opened this session) decides what is dropped. Unverified beyond that: investigate first.

## Context

- Different mechanism from the interface guard and private-field fixes; filed investigate-first per Directive #3.
