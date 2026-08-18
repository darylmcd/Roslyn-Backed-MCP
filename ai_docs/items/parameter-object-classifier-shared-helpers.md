# parameter-object-classifier-shared-helpers — dedupe the two use-classifiers

**row:** `parameter-object-classifier-shared-helpers` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:427`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:506`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:548`

## Acceptance

- [ ] A single `IsIncrementOrDecrementTarget(ExpressionSyntax)` helper replaces the pre/post increment-decrement parent test copy-pasted between `ClassifyVariableRequiredUse` (`:427-438`) and `ClassifyValueTypeMutation` (`:548-559`).
- [ ] `semanticModel.GetOperation(reference, ct)` is computed and unwrapped ONCE and passed into both classifiers, rather than re-invoked at `:506` for the same node.

## Evidence

Both duplications introduced/observed in the PR #1267 diff; the two classifier copies differ only in the returned label string.

## Context

Two low-severity code-quality findings from PR #1267 (`parameter-object-value-type-mutation-semantics`). Advisory — neither blocked that PR.
