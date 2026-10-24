# fork-source-fixture-initialization-cleanup-ownership — Close external fixture owners after initialization failure

**row:** `fork-source-fixture-initialization-cleanup-ownership` · **pri:** `Medium` · **size:** `S` · **deps:** `fork-source-fixture-cleanup-failure-propagation`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:394` — `ForkFixture.CreateAsync`, current main14fbdc02.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:412` — externally created manager/store/gate, registered as existing instances418-421.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:440` — actual load; initialization catch448-452 disposes provider then deletes root.

## Acceptance

- [ ] On actual load, preview or service initialization failure, shut down externally owned manager/gate/watcher resources before deleting owned material; handle every owner actually created by the factory.
- [ ] Preserve the initialization failure and independent cleanup failures; reuse the shared failure-preserving cleanup contract without introducing a production test hook.
- [ ] Add an actual failed-load regression using the external-manager instance graph. Assert all admitted owner tasks finish, dependencies are released once, and material is retained when ownership remains uncertain.
- [ ] Observe the regression fail on the old factory; preserve all successful fixture initialization and lifecycle assertions.

## Evidence

- Immutable32de7e8a factory constructs external manager/gate412-417 and registers instances418-421. Failed load442 enters catch450-454, which disposes only the provider and deletes the root. Main14fbdc02 lines after434 are two lower; instance ownership is identical.
- Existing instance registrations do not transfer ownership to the provider. Cleanup currently omits those external owners; provider cleanup failure can also mask the initialization failure.
- Distinct initialization-failure regression. Depends on the shared cleanup row; PR1823 and restore do not depend on this row.
- Preserved experimental cleanup source and unresolved findings are referenced by the dependency row; re-derive the implementation from actual owner contracts.
