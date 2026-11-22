# relocation-moved-declaration-sibling-import — The new file of a moved type lacks a using for siblings left in the old namespace

**row:** `relocation-moved-declaration-sibling-import` · **pri:** `Medium` · **size:** `S` · **verified:** `2ae6a73748e9a2433e002f3ecf78ab90f620c7b9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] A moved type (class, enum or delegate) that references a type left in the old namespace gets `using Old;` in its new file, or the preview refuses; 0 compile errors, red-first test (the preview solution fails to compile today).

## Evidence

- Probe (e) of the relocation-support-enum-delegate-types slice 1 plan on the base commit with a class: the new file lacks `using Old;`.
