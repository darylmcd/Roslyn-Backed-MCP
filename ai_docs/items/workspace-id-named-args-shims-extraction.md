# workspace-id-named-args-shims-extraction — Name workspace-sensitive shim and extraction arguments

**row:** `workspace-id-named-args-shims-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/BuildTestToolsShimTests.cs`
- `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the three files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and all three targeted test classes to pass.

## Evidence

The live census found positional shim, observability, and extraction calls whose required arguments follow `workspaceId`.

## Context

Split from `workspace-id-optional-named-argument-prerequisite`.
