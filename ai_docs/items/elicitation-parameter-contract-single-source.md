# elicitation-parameter-contract-single-source — Single-source workspace elicitation parameter names

**row:** `elicitation-parameter-contract-single-source` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/ElicitationAllowlistPolicy.cs`
- `tests/RoslynMcp.Tests/StructuredCallElicitationCoordinatorTests.cs`

## Acceptance

- [ ] Define the elicitable tool and `workspaceId`/`path` parameter names once and consume that contract in filtering, coordination, and allowlisting.
- [ ] Preserve the current fail-closed allowlist and confirmation behavior.
- [ ] One table-driven contract test proves all three consumers accept and reject the same tool/parameter pairs.

## Evidence

- The three middleware components duplicate the same workspace-load tool and parameter literals; one comment explicitly describes the list as a mirror.
