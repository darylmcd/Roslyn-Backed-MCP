# git-fixture-runner-output-drain-budget — Bound Git fixture output draining

**row:** `git-fixture-runner-output-drain-budget` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs` — RunGitCapture bounds process exit but synchronously waits on unbounded stdout/stderr tasks after exit.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs` — reusable exited-parent/inherited-pipe regression pattern.

## Acceptance

- Give Git fixture invocations a finite combined process-exit and output-drain budget and observe both readers on failure.
- Preserve the existing synchronous fixture entry points while moving process lifetime ownership through the shared executable runner or an equivalently bounded implementation.
- Add a controlled retained-pipe regression with unconditional descendant cleanup; keep argument and nonzero-exit behavior unchanged.

## Evidence

2026-09-15 direct review: RunGitCapture starts un-cancellable ReadToEndAsync tasks, waits 30 seconds for process exit, then calls GetAwaiter().GetResult on both readers without a drain deadline. Its exit-timeout branch throws without observing either reader. This is a separate runner from PwshScriptRunner.
