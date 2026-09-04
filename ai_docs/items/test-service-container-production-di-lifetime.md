# test-service-container-production-di-lifetime — Align test composition and lifetime ownership

**row:** `test-service-container-production-di-lifetime` · **pri:** `Medium` · **size:** `S` · **deps:** `test-shared-gate-rate-limit-isolation, test-service-container-two-phase-construction-cycle`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs`
- `tests/RoslynMcp.Tests/TestAssemblyFixtureTests.cs`

## Acceptance

- [ ] The test composition root reuses production registrations with explicit test overrides rather than duplicating the full service graph.
- [ ] Exposed services are the provider's singleton identities and test-specific execution options remain intact.
- [ ] The provider, execution gate, workspace manager, and file watcher have one explicit, idempotent disposal owner.
- [ ] Standalone container tests no longer leak the gate or manager.

## Regression

Resolve a standalone fixture, prove representative singleton identities, dispose it twice, and observe deterministic disposed behavior from the gate and workspace manager without duplicate-disposal failure.

## Evidence

The handwritten test graph is about 240 lines and already drifted from production DI identity. `TestAssemblyFixture.DisposeAsync` disposes the manager (and watcher) but not `WorkspaceExecutionGate`; a standalone container identity test disposes neither.


> Size correction (2026-09-04): canonical row size is `S`; the initial detail header said `M` before the anchor-derived size warning was reconciled.
Cold review of PR #1464 confirmed the deferred gate holder is a safe interim cycle break, but it remains a bespoke two-phase construction protocol. Replace it with the production registration provider and explicit test overrides under this row; preserve the new identity and disposal regressions.
