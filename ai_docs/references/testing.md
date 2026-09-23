# Testing Reference

<!-- purpose: Test commands, patterns, and coverage expectations for this repo. -->

## Commands

- Build/test/CI commands and shard reproduction: [runtime.md](../runtime.md#task-runner) and [AGENTS.md § Validation runtime](../../AGENTS.md#validation-runtime).
- The test project invokes `eng/prepare-test-fixtures.ps1` before `VSTest`, so `dotnet test` restores every owned `samples/**/*.slnx` fixture before discovery.

## Coverage

- Full release validation (`./eng/verify-release.ps1`) runs tests with **XPlat Code Coverage** and writes Cobertura XML under `artifacts/coverage/`.
- Baseline numbers and test-priority notes: `docs/coverage-baseline.md`.
- CI artifact: `code-coverage` (see `CI_POLICY.md`).

## CI Shards And Timing Evidence

| Contract | Location |
|---|---|
| Deterministic compiled-class planner | `eng/get-test-shard-plan.ps1` |
| Empty-filter failure | `eng/ci.runsettings` (`TreatNoTestsAsError=true`) |
| Shard entry point | `eng/verify-release.ps1 -TestShardIndex <zero-based> -TestShardCount <count>` |
| CI non-owner shard lane | Add `-TestShardOnly` to skip platform-neutral policy checks and publish/hash; do not use it as the standalone release gate |
| Per-leg duration/failure evidence | `artifacts/test-results/*.trx`; hosted artifact `test-results-<leg>` |
| Bounded job-summary renderer | `eng/summarize-test-results.ps1 -ResultsPath artifacts/test-results` |
| Offline per-image shard-skew collector | `eng/collect-hosted-shard-timings.ps1 -ResultsRoot <dir> -LegManifest <json>` |
| Standing shard-weighting decision | `CI_POLICY.md` section "Hosted Shard Weighting Decision" |

- Keep local `just ci` unsharded; it proves the complete suite in one invocation.
- Summed TRX case duration alone is not a partition signal on hosted runners (same-leg run-to-run swing can exceed between-leg spread).
- Quote wall-time skew and summed case duration as separate metrics; `eng/collect-hosted-shard-timings.ps1` emits separate columns and refuses fewer than `-MinimumSamples` (default 5) runs per hosted image.
- Model each hosted image on its own evidence. Never merge images into one profile and never feed a local-machine timing into a hosted profile.
- For CI, require nonempty shards whose class sets are disjoint and whose union equals discovery.
- Use exact `ClassName` filters. Do not revive per-source-regex or `FullyQualifiedName~` slicing.
- Diagnose slow tests from repeated TRX durations. A timeout attribute or method count is not runtime evidence.

## Test Project

- `tests/RoslynMcp.Tests/`

## Guidance

- Prefer integration coverage for end-to-end workspace and tool behavior.
- For docs-only changes, run lightweight link/reference checks at minimum.
- For contract/surface changes, include or update tests in the same branch.
- Report unsupported-platform cases as inconclusive/skipped, never as a passing early return.
- Release any deliberately blocked/non-cooperative worker before the test returns; otherwise isolate it in a disposable child process.
