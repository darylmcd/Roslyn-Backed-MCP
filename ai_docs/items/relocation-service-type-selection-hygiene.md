# relocation-service-type-selection-hygiene — Tidy NamespaceRelocationService type selection and add enum/delegate edge tests

**row:** `relocation-service-type-selection-hygiene` · **pri:** `Low` · **size:** `S` · **verified:** `2ae6a73748e9a2433e002f3ecf78ab90f620c7b9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Type selection is arity-aware (a generic delegate named like a class is selected by its arity or the ambiguity message names each candidate once, not the same file twice), red-first test.
- [ ] The raw InvalidOperationException in ApplyNamespaceRewriteAsync and FindTypeInNamespaceAsync use the repo exception helpers (InvalidOperationErrors) and the construction baseline stays unchanged or shrinks.
- [ ] Enum and delegate with attributes, an XML doc comment and `#if` wrapping relocate correctly (DataRow tests).

## Evidence

- Cold implementation review of relocation-support-enum-delegate-types slice 1, follow-ups; worker sketches.
