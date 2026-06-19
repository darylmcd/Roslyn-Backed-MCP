---
category: Fixed
---

- **Fixed:** Stabilized the flaky `ExternalEditStalenessTests` FileSystemWatcher test — it now awaits an event-driven `IFileWatcherService.WaitForStaleAsync` signal (a `TaskCompletionSource` flipped inside the staleness mark) instead of a 2000 ms wall-clock poll, with a bounded ceiling and a dropped-event re-touch guard. A slow or dropped OS watcher event no longer fails `verify-release.ps1` (which now runs in `publish-nuget.yml` on ubuntu), so it can no longer fail the NuGet + MCP-registry publish. Closes `ci-flaky-fswatcher-staleness-test`.
