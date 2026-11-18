# dotnet-runner-concurrent-descendant-stdout-loss — Lost parent stdout while concurrent owned descendants hold pipes

**row:** `dotnet-runner-concurrent-descendant-stdout-loss` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:148-222` — concurrent held-pipe fixture and exact parent output assertion.
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:36-42,145-160,260-301` — capture, post-exit drain grace and decoder cancellation.
- `tests/RoslynMcp.Tests/Helpers/ProcessPipeReader.cs:65-92` — sole-reader Windows transport and native pipe availability query.

## Acceptance

- [ ] Reproduce RED: two concurrent runs whose parents write one line and exit while a descendant holds both pipes; observe exit 0 with empty `StdOut` (CI shape) at least once under load, or prove by source that buffered output can be discarded when the drain grace expires.
- [ ] Fix the root cause so bytes the parent wrote before exit are always returned, independent of scheduling delay; do not lengthen the grace period, the test timeout, or weaken any assertion.
- [ ] Cover stdout and stderr; keep the existing descendant-isolation and bounded-drain guarantees.
- [ ] Qualify on hosted Windows and Linux legs.

## Evidence

- Hosted run 40369031351, job 121734597120 (windows-hosted-1-of-4, PR #1850 at 2026-10-26T17:33Z): `Failed RunAsync_ConcurrentOwnedDescendants_HoldPipesWithoutDisruptingEachOtherAsync [7 s]` — `expected substring: "parent-output"`, `actual: ""`; `ExitCode` assertion before it passed.
- PR #1850 changed only `Microsoft.Extensions.*` package pins (no runner code), so the diff did not cause it; it has not been reproduced on the base commit. Not in `ai_docs/known-flakes.md`.

## Context

- Found while remediating dependabot PRs #1850–#1852 (2026-10-26). Independent; no dependency.

## Notes

2026-11-17 current evidence: PR 2131 run 44104410433 job 134112846796 returned exit 0 and empty StdOut for the short ASCII parent marker. On clean c705e2e1, the Release pipe class passed 9/9. A separately gated base-binary probe delivered 4096 UTF-8 bytes, parked the next raw read and cancelled only grace. Expected 2048 characters, captured zero, exit 1. This reproduces decoder cancellation loss. The short ASCII hosted witness still needs a native reproduction. Require a fenced native marker reproducer and qualify the complete finite-drain design across Windows, Linux and macOS before selection. Keep caller cancellation, bounded tail/decoding, reader disposal, early-kill classification and stable build/test/restore contracts. Reuse the sole-reader Windows test transport where appropriate and qualify platform-specific final available-byte snapshots. Consider the existing early-kill and unobserved-reader companion rows.

Explicitly qualify Windows thread-pool worker capacity in the command runner itself. The related Git and script consumers have a separate worker-capacity row. Qualify deterministic disposal under dotnet-command-runner-unobserved-failure-paths.
