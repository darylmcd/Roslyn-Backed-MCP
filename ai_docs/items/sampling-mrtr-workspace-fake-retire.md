# sampling-mrtr-workspace-fake-retire — SamplingMrtrWireTests keeps a private list-only copy of FailClosedWorkspaceManagerStub

**row:** `sampling-mrtr-workspace-fake-retire` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`

## Acceptance

- [ ] `SamplingMrtrWireTests` deletes its private `SamplingWorkspaceManager` (`:1142-1179`) and constructs `FailClosedWorkspaceManagerStub` with the same initial status (`:92`). The sampling callback keeps calling `ReplaceWith` (`:101`).
- [ ] The class header comment (`:26-34`) names the shared stub instead of `SamplingWorkspaceManager`.
- [ ] The private fake answered `ContainsWorkspace` and `IsStale` with `false`, and the stub throws. The harness registers `PassThroughWorkspaceExecutionGate` (`:886`), so neither should be reached. If a run shows otherwise, add a dep on `unused-code-analyzer-test-harness-wave-1` (which adds those handlers) rather than restoring a permissive default.
- [ ] Assertions are unchanged, and the class keeps its test count and passes.

## Evidence

- At `716b5e20`, `SamplingWorkspaceManager` (`SamplingMrtrWireTests.cs:1142-1179`) is a list-only copy of `tests/RoslynMcp.Tests/Helpers/FailClosedWorkspaceManagerStub.cs`: the same `params WorkspaceStatusDto[]` list, `Volatile` read, `ReplaceWith` and no-op event subscriptions. It differs only in answering `ContainsWorkspace` / `IsStale` with `false` where the stub throws. Only `:92` constructs it.
- Found by the PR #1665 review of the server-probe batches. The full recount of private workspace-manager fakes is in `server-probe-test-doubles-batch-1`.

## Context

- Standalone: the stub already exists.
- `scaffold-warning-mrtr-typed-state` and `scaffold-syntactic-preflight-candidate-enumeration-decomposition` also anchor `SamplingMrtrWireTests.cs`. Sequence by hotspot if they are picked up together.
