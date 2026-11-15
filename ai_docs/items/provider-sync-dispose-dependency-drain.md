# provider-sync-dispose-dependency-drain — Qualify synchronous container shutdown during owned workspace cleanup

**row:** `provider-sync-dispose-dependency-drain` · **pri:** `Medium` · **size:** `M` · **verified:** `e6c61174610c8d045d559f0621b7b02d31cecbfa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` — synchronous Dispose starts deferred manager cleanup.
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` — container ownership of the watcher and manager lifetime.

## Acceptance

- [ ] With actual AddRoslynServices registrations, pause an admitted manager retirement during watcher removal and synchronize direct ServiceProvider.Dispose with the paused work.
- [ ] Prove that provider-owned watchers stay live through manager cleanup, or define and qualify the supported async disposal entry point. Preserve asynchronous host shutdown, external ownership and failure visibility.

## Evidence

- 5.x shutdown implementation review. The manager can defer synchronous cleanup while the root provider continues synchronous disposal of captured dependencies. The supported Generic Host uses asynchronous disposal; this source-only finding concerns direct synchronous consumers.
