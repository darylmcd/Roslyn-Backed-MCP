# test-run-current-invocation-trx-isolation — Bind TRX parsing to the current run

**row:** `test-run-current-invocation-trx-isolation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:377`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`

## Acceptance

- [ ] Collect only TRX files attributable to the current `dotnet test` invocation; do not recursively aggregate historical files from the target working directory.
- [ ] A build failure before test execution returns zero current-run results plus the typed failure envelope even when unrelated TRX files already exist under `TestResults`.
- [ ] Preserve the bounded stdout-reported `Results File:` fallback for hosts that ignore the explicit results directory.

## Evidence

- A live focused `test_run` failed during fixture compilation but reported 110,645 stale results and unrelated failures from prior runs because `CollectTrxFiles` scanned the working-directory `TestResults` tree.
