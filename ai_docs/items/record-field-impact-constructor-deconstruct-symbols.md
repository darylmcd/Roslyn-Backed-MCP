# record-field-impact-constructor-deconstruct-symbols — Bind record impact sites to the affected symbols

**row:** `record-field-impact-constructor-deconstruct-symbols` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs:332-416`
- `tests/RoslynMcp.Tests/RecordFieldAdditionImpactTests.cs`

## Acceptance

- [ ] Report sites that bind to the affected primary constructor or synthesized Deconstruct. Exclude same-arity independent constructors and Deconstruct members, and recognize compiler-bound array and custom foreach sites.

## Evidence

- Source review at `34a87d9c`. Construction classification compares argument count to positional arity. The foreach classification guesses the element type from generic arguments rather than bound deconstruction.
