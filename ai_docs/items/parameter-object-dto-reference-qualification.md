# parameter-object-dto-reference-qualification — Bind generated DTO references across namespaces

**row:** `parameter-object-dto-reference-qualification` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Emit a semantically bound DTO type reference in the target declaration and every rewritten caller when the DTO namespace differs.
- [ ] Preserve ordinary same-namespace output without unnecessary qualification churn.
- [ ] Add an explicit different-DTO-namespace and cross-namespace caller regression through `apply_with_verify`, asserting applied status and equal pre/post compiler errors.

## Evidence

- Rewrites currently emit the unqualified new type name; an explicit DTO namespace or caller without a matching `using` produces CS0246.
