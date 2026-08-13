# parameter-object-target-method-contract-validation — Validate parameter-object target method contracts

**row:** `parameter-object-target-method-contract-validation` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`PreviewParameterObjectAsync`, `EnforceParameterShapeRefusals`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Define and enforce supported method kinds and contracts before collecting callers or building a preview.
- [ ] Refuse constructors, operators/accessors, overrides, interface implementations, and extern/PInvoke declarations unless declaration and all dispatch contracts can be rewritten atomically.
- [ ] Return an actionable refusal naming the unsupported contract shape and store no preview token.
- [ ] Add one table-driven regression proving unsupported targets cannot emit a compile-breaking partial rewrite.

## Evidence

- The service currently rejects only local functions; constructor calls are not invocation syntax, and override/interface/extern signatures cannot be changed in isolation without breaking their external contract.

## Acceptance amendment (2026-08-13 adversarial review)

- Include partial definition/implementation pairs, whose legal declarations may use different parameter names.
- Include virtual and abstract dispatch roots in addition to explicit overrides and interface implementations.
