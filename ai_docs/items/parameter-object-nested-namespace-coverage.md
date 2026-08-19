# parameter-object-nested-namespace-coverage — cover the nested-namespace qualification branch

**row:** `parameter-object-nested-namespace-coverage` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1215`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1487`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:1002`

## Acceptance

- [ ] A test places the target method and a caller in `SampleLib.Inner` with the DTO defaulting to `SampleLib`, asserting both the declaration parameter type and the object creation stay unqualified — the outward-lookup branch (`contextNamespace.StartsWith(dtoNamespace + ".")`) currently has no coverage.
- [ ] The test lives in the existing `ParameterObjectPreviewTests` regression set and names the branch it exercises.
- [ ] `ResolveDtoTypeReference` is hoisted out of the `ReplaceNodes` callback (per-document, or memoized per enclosing namespace declaration) — the answer is per-document constant because the rewrite mutates neither usings nor namespaces, but today the ancestor/using walk repeats for every call site in the document.

## Evidence

The three return paths of `ResolveDtoTypeReference` traced against the test file: equal-namespace is asserted by `CrossNamespaceCaller_NoUsingDirective_...`, the using-directive path by the pre-existing `CrossProject_RewritesCallSiteWhenReferenceExists` (`new SumArgs(1, 2, 3)`), but no test produces a context namespace strictly nested under the DTO namespace.

## Context

Two low-severity findings from PR #1271 (`parameter-object-dto-reference-qualification`) code-quality review — one coverage gap, one redundant per-call-site walk. Advisory; neither blocked that PR.
