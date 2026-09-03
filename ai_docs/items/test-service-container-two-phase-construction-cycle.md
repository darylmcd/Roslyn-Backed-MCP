# test-service-container-two-phase-construction-cycle — Order-dependent gate/manager cycle

**row:** `test-service-container-two-phase-construction-cycle` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [ ] The `WorkspaceExecutionGate` / `WorkspaceManager` cycle is resolved without a mutable local captured by a `Lazy<>` that is assigned after the constructor call, OR the ordering requirement is enforced so reordering fails loudly at compile time rather than at runtime.
- [ ] The throwing "resolved before initialization" guard is either unreachable by construction or covered by a regression.

## Evidence

Verified against `main` on 2026-09-03, `:77-87`: a `WorkspaceExecutionGate? workspaceExecutionGate =
null` local is captured by a `Lazy<IWorkspaceExecutionGate>` passed INTO the `WorkspaceManager`
constructor, then assigned on the following statement. The guard
`workspaceExecutionGate ?? throw new InvalidOperationException("The test workspace execution gate was
resolved before initialization.")` is correct only because of statement ordering — moving the
assignment silently arms the throw.

Production resolves the same cycle declaratively through DI, so the fixture carries a hand-rolled
two-phase construction the real composition root does not need.

## Context

Surfaced by the executor of PR #1431 (Directive #3); pre-existing, not introduced by it.

[source: 2026-09-03 backlog-remediate PR #1431]
