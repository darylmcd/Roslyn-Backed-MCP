# dotnet-runner-concurrent-descendant-stdout-loss — Lost parent stdout while concurrent owned descendants hold pipes

**row:** `dotnet-runner-concurrent-descendant-stdout-loss` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:117` — `RunAsync_ConcurrentOwnedDescendants_HoldPipesWithoutDisruptingEachOtherAsync`; assertion at `:189` (`StdOut` contains `parent-output`).
- `DotnetCommandRunner.RunAsync` / `ReadBoundedAsync` and `PostExitDrainGracePeriod` — post-exit bounded drain of stdout/stderr while a descendant keeps the pipe handles open.

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
