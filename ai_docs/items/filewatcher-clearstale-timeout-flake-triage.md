# filewatcher-clearstale-timeout-flake-triage — replace the ClearStale awaiter's wall-clock bound with a deterministic signal

**row:** `filewatcher-clearstale-timeout-flake-triage` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:405` (`parked.WaitAsync(TimeSpan.FromMilliseconds(UnblockBoundMs))`)
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` (`WaitForStaleAsync` / `ClearStale`)
- `ai_docs/known-flakes.md` (registry entry this row is cited by)

## Acceptance

- [ ] `ClearStale_ReleasesAwaiterParkedOnPriorSignal_RatherThanStrandingIt` no longer depends on a fixed wall-clock bound: it asserts the parked awaiter's resolution via a counter or signal (e.g. awaiting the parked task with a generous cancellation token, or observing an explicit release count), so host load cannot fail it.
- [ ] The corresponding entry is removed from `ai_docs/known-flakes.md` once the test is deterministic — the registry lists only tests still known to be flaky.

## Evidence

- `ai_docs/known-flakes.md` registers this test as timing-sensitive: `System.TimeoutException` at `ExternalEditStalenessTests.cs:405`. Observed failing on PR #1034 (unrelated diff, passed on rerun) and on PRs #1086/#1085 (2026-07-22 — a `Directory.Packages.props`-only and an `ApplyWithVerifyTool.cs`-only diff, neither touching `FileWatcherService` or this test); 3/3 local reruns passed in ~22ms with the runner idle.

## Context

Re-filed on 2026-08-10. `ai_docs/known-flakes.md` cited this row id and asserted it "stays open — only the registration half of its acceptance is done here", but the row was absent from `ai_docs/backlog.md`, so the underlying fix had become untracked while the registry still pointed at it. A dangling citation in the flake registry is worse than no citation: it implies the work is tracked when nothing is.

Re-triaged the same day against the shared-temp-root fixture race (`test-temp-root-shared-cleanup-race`) and confirmed **unrelated** — that race throws `DirectoryNotFoundException` at fixture-write time, whereas this is a `TimeoutException` inside a timing-bounded await. The registry's "runner load" attribution stands.

Same shape as the sibling entry `WorkspaceExecutionGateTests.AutoReload_ResetsTimeoutBudget_ToolActionGetsFullBudget`; if both are fixed, prefer one shared deterministic-signal helper over two bespoke widenings.

## Notes

- Do NOT simply widen `UnblockBoundMs` — that lowers the failure rate without removing the wall-clock dependency, and the registry entry would have to stay.
