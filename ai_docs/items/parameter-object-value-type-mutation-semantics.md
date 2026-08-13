# parameter-object-value-type-mutation-semantics — Preserve grouped value-type mutation semantics

**row:** `parameter-object-value-type-mutation-semantics` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`EnforceGroupedParametersAreReadOnlyAsync`, `ClassifyVariableRequiredUse`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Detect value-type instance mutations reached through grouped parameters, including mutating member calls and nested field/property assignments.
- [ ] Preserve the original local-copy behavior with an explicit local and write-back strategy, or refuse before preview with the affected parameter and use location.
- [ ] Do not reject reference-type member mutation or array-element mutation that remains semantically equivalent through the DTO property.
- [ ] Add one apply regression where a mutable struct is mutated and then read; runtime result and compile errors remain unchanged.

## Evidence

- Rewriting `value.Mutate(); return value.State;` to repeated positional-record property reads compiles but mutates a temporary struct copy, silently changing the returned state.
