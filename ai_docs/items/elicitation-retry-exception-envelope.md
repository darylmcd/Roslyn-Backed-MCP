# elicitation-retry-exception-envelope — recovery-path exceptions must return the standard tool error envelope

**row:** `elicitation-retry-exception-envelope` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`

## Acceptance

- [ ] Failed recovery/dispatch exceptions converted into the same structured `CallToolResult` error shape used by normal dispatch failures; successful recovery behavior preserved
- [ ] Regression: fixture `workspace_load` or retried dispatch throwing during missing-`workspaceId` recovery asserts a structured `CallToolResult` with error content/schema hint rather than an escaped exception

## Evidence

- Observed while implementing `elicitation-allowlist-workspaceid-recovery`; Standing Engineering Directive #3.

## Context

`StructuredCallToolFilter.TryRecoverMissingWorkspaceIdAsync` can let exceptions from `workspace_load` or the retried tool dispatch escape instead of returning the standard tool error envelope.
