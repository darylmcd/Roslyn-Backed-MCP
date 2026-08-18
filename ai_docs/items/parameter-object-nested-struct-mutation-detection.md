# parameter-object-nested-struct-mutation-detection — refuse mutating calls through nested struct fields

**row:** `parameter-object-nested-struct-mutation-detection` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:519`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:544`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:183`

## Acceptance

- [ ] `ClassifyValueTypeMutation` refuses the preview when a non-readonly instance member is invoked on an all-value-type receiver chain rooted at a grouped parameter (e.g. `param.Inner.Mutate()`), naming the parameter and source line.
- [ ] A reference-typed segment anywhere in the chain (e.g. `param.RefField.Mutate()`) keeps the preview eligible.
- [ ] Regression tests cover both shapes plus `param.State++` and `param.Inner.Field = 1`.

## Evidence

Traced at code level in the PR #1267 diff: the receiver-chain switch (`:527`) matches only `MemberAccessExpressionSyntax` / `ElementAccessExpressionSyntax`, so for `param.Inner.Mutate()` the walk stops at `param.Inner.Mutate`, whose parent is an `InvocationExpressionSyntax`, falls past the assignment/increment tail, and returns null — while the operation-based check at `:510` requires the parameter reference itself to be `invocation.Instance`.

## Context

**PR #1267 (`parameter-object-value-type-mutation-semantics`) fixed the depth-1 shapes only.** The mutation-on-a-copy defect that initiative set out to close therefore persists one level deep. Surfaced by that PR's code-quality review as a medium `dead-path` finding; advisory, so it did not block the merge. The companion `untested-critical-path` finding is the corroborating signal — the new tests exercise only `called.Mutate()` / `written.State = 1`, leaving the multi-segment chain loop and its value-type/reference-type discriminator untested.
