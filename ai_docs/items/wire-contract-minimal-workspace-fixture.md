# wire-contract-minimal-workspace-fixture — Slim protocol-only workspace setup

**row:** `wire-contract-minimal-workspace-fixture` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/StructuredContentWireContractTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceResourceListNotificationWireTests.cs`

## Acceptance

- [ ] Introduce one owned minimal MSBuild fixture sufficient for workspace load/reload/close and schema-bearing tool/resource responses.
- [ ] Retain legacy and current protocol sessions plus every existing empty/single/multiple-workspace and notification assertion.
- [ ] Prove fixture teardown leaves no workspace, transport, process, or temp-directory state.
- [ ] Repeated isolated timing is materially lower than the current approximately 10-second and 9-second class runs without weakening assertions.

## Evidence

The two valid wire suites perform multiple complete protocol sessions and full sample-solution loads. Their 39.80-second and 32.45-second full-run durations fell to about 10 and 9 seconds alone, showing both avoidable fixture cost and strong contention sensitivity.
