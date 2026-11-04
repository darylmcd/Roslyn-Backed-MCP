# archive-old-reports-tests-process-ownership — Own archive script test processes

**row:** `archive-old-reports-tests-process-ownership` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/ArchiveOldReportsScriptTests.cs:231`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`

## Acceptance

- [ ] Replace sequential redirected-stream reads and unowned timeout cleanup with the shared bounded runner for every archive-script call.
- [ ] A red-first stderr-pressure/timeout test proves concurrent drains, child exit, and no unobserved reader task; keep existing assertions and deadlines.

## Evidence

- Cold review of PR #1968 found a private archive-script process helper at `ArchiveOldReportsScriptTests.cs:231-235` with the same sequential-pipe and timeout-ownership pattern repaired in the renderer tests.
