# test-temp-root-reap-tests-delete-live-sibling-runs — TestTempRootTests reap against the real shared parent and delete concurrent hosts' live run-* dirs

**row:** `test-temp-root-reap-tests-delete-live-sibling-runs` · **pri:** `Medium` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/TestTempRootTests.cs:100-125`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestTempRoot.cs:62`

## Acceptance

- [ ] ReapAbandonedRuns accepts the parent directory (or the tests use an isolated parent) so no test enumerates or deletes the real SharedParent.
- [ ] ReapAbandonedRuns_NeverRemovesThisRun... no longer anchors a +366-day clock over the shared parent; a test proves a sibling live run survives the reap tests.

## Evidence

- TestTempRoot.SharedParent is the real `%TEMP%\RoslynMcpTests`; `ReapAbandonedRuns_NeverRemovesThisRunEvenWhenItLooksStale` passes `UtcNow + StaleRunAge + 365d`, classifying every sibling run as abandoned and deleting it. Matches the DirectoryNotFoundException for SampleLib/Dog.cs at test start in a full run (undo-b report, sketch 1) and shared-expr report. Distinct from test-temp-reaper-live-owner (production reaper lacks owner evidence); that row's fix also does not isolate these tests.
