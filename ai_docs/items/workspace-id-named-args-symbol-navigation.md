# workspace-id-named-args-symbol-navigation — Name workspace-sensitive navigation arguments

**row:** `workspace-id-named-args-symbol-navigation` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/FindOverloadsTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceDispatchValidationPrecedenceTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the three files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and all three targeted test classes to pass.

## Evidence

The live census found positional navigation/workspace calls whose required arguments follow `workspaceId`.

## Context

Split from `workspace-id-optional-named-argument-prerequisite`.
