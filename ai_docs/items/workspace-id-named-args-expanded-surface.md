# workspace-id-named-args-expanded-surface — Name workspace-sensitive expanded-surface arguments

**row:** `workspace-id-named-args-expanded-surface` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.ToolContract.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the two partial-class files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and the expanded-surface test class to pass.

## Evidence

The live census found positional expanded-surface calls whose required arguments follow `workspaceId`.

## Context

Split from `workspace-id-optional-named-argument-prerequisite`.
