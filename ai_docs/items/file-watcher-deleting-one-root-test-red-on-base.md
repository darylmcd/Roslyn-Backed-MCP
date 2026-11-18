# file-watcher-deleting-one-root-test-red-on-base — Fix DeletingOneRoot_RetiresOnlyThatWorkspaceAndKeepsSiblingWatched

**row:** `file-watcher-deleting-one-root-test-red-on-base` · **pri:** `Medium` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs`:78-120
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`

## Acceptance

- [ ] Run the test alone on current main (after PR 2146 rewrote this class) on the dev host; if it still times out, identify whether the root-missing event is never raised or raised for the wrong workspace, and fix the cause (test fence or service).
- [ ] If it passes, record three clean runs and close with that evidence.

## Evidence

- iface-extraction-b report (PR 2148): full run failure; re-run alone on the PR tree still failed; "Diagnosed on base: detached origin/main aafbd01a worktree ... same test alone: FAILED the same way (base-watcher.log, 30 s TimeoutException at FileWatcherSharedWatcherTests.cs:101)".
- Base aafbd01a predates PR 2146 (79691355), which changed this class; the C: disk was near full that day (possible confound).
