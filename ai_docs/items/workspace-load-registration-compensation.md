# workspace-load-registration-compensation — Compensate failed owned workspace registration

**row:** `workspace-load-registration-compensation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.ForkInvocation.cs`
- `tests/RoslynMcp.Tests/ForkInvocationManagerAuthorityTests.cs`

## Acceptance

- [ ] A watcher or current-source failure after registration removes only the exact registered session, unwatches it, disposes owned resources and returns its capacity slot.
- [ ] Preserve the primary failure or cancellation and report bounded cleanup outcomes; no-primary cleanup failures remain observable.
- [ ] Preserve unrelated/replacement sessions; prove subsequent real native loads can use the returned slot.
- [ ] Observe guarded old-behavior failure and fresh positive controls before release.

## Evidence

- Source review 2026-10-16: LoadCore marked sessionAdded before Watch, then disposed on failure without removing registration or releasing its slot. New invocation current-source checks expose the same post-registration failure boundary.
- Source correction and real native Watch/source-close regression rows are prepared; parent owns serialized runtime. No runtime acceptance or closure claimed.

| 2026-10-16 bounded native evidence | Result | Limit |
|---|---|---|
| Fresh first focused snapshot | Registration Watch-throw/source-close controls passed within focused82; subsequent fixed cleanup/manager11 also passed. | Complete aggregate/merge remains pending. |
| Localized old registration | Both native rows failed the stale-registration assertion after actual watcher/post-registration premises. Manager source restored byte-exact (Before=After5EF93D...62B8). | The guard retained the new required invocation API; only old post-registration compensation was restored. |
