# watcher-overwrite-negative-signal-fence — Synchronize the overwrite callback before asserting no missing notification

**row:** `watcher-overwrite-negative-signal-fence` · **pri:** `Low` · **size:** `S` · **verified:** `baf8f8026d481cf8218396142e64525a158483f3`

## Anchors

- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs` — RenamingAnotherFileOverTheWorkspaceFile_DoesNotRaiseRootMissing.
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` — tree callbacks and the stale signal ingress.

## Acceptance

- [ ] Observe a signal that fences completion of the overwriting rename callback across supported Windows and Linux notifications.
- [ ] Preserve positive missing-root controls and demonstrate that a regression raising the missing callback on overwrite fails regardless of callback scheduling. Remove the fixed delay once the fence is proved.

## Evidence

- Cold review of the inotify child-process repair found that the unrelated overwrite test waits for the shared stale signal and then sleeps 500 ms. That signal can also come from the overwrite itself, so it cannot identify the later marker callback on its own.
