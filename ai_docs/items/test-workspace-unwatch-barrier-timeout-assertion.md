# test-workspace-unwatch-barrier-timeout-assertion — Report watcher fixture barrier expiry

**row:** `test-workspace-unwatch-barrier-timeout-assertion` · **pri:** `Low` · **size:** `S` · **deps:** `fork-source-fixture-cleanup-failure-propagation`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs` — `ObservedWatcher.Unwatch`, `ReleaseUnwatch`.

## Acceptance

- [ ] Check the existing bounded unwatch barrier wait result and surface timeout as an explicit fixture failure.
- [ ] Add a deterministic expiry regression and a successful release control; preserve the existing behavioral deadline and real watcher/disposal assertions.
- [ ] Preserve the primary test failure and ownership cleanup when the barrier expires.

## Evidence

- PR1823 initial-cancellation implementer observed `ReleaseUnwatch.Wait(TimeSpan.FromMinutes(2))` discarding its Boolean result on the inherited fixture.
- Distinct pre-existing barrier-expiry mechanism; no restore dependency. Recheck live symbol/line anchors and causal evidence when selecting this row.
