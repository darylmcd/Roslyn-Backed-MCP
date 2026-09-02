# ci-hosted-shard-duration-balancing — Rebalance hosted shards from repeated timings

**row:** `ci-hosted-shard-duration-balancing` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/get-test-shard-plan.ps1`
- `eng/summarize-test-results.ps1`
- `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] Collect at least five successful per-leg TRX runs for each hosted OS/image and quantify wall-time skew separately from summed case duration.
- [ ] Adopt duration weights only when repeated evidence shows material skew; keep deterministic discovered-case weights as a fail-closed fallback.
- [ ] Model hosted Windows and hosted Linux independently; do not mix local-machine timings into either profile.
- [ ] One deterministic missing/stale-profile regression proves no class is omitted, overlapped, or assigned to an empty shard.

## Evidence

Static discovered-case weights currently split the compiled classes evenly, but workspace-heavy and serialized fixtures have heterogeneous costs. The new hosted-only topology first needs repeated per-image evidence; a local Windows profile is not representative of GitHub-hosted Windows and cannot safely drive its partition.
2026-08-24 PR #1325 calibration: two hosted Windows jobs completed in 19m50s and 16m29s; their TRX cumulative class durations were 2,549.4s and 1,764.0s despite equal static weights. The unchanged static planner projects four shards at 1,258.7s, 865.6s, 785.5s, and 1,403.5s from the same evidence, so the immediate topology expanded to four. Require at least five successful same-image samples before adding an OS-specific duration profile.

## Amendment — 2026-09-02 (cold plan-deepener; evidence gate RESOLVED — premise refuted, no code shipped)

**The evidence gate this row was waiting on is now satisfied, and the measurement says DO NOT adopt duration weights.**

Eight successful code-PR `ci` runs sit inside the 14-day artifact window (`.github/workflows/ci.yml:247`) — 33657245113, 33652700281, 33645215349, 33639979002, 33552418343, 33546400118, 33538827745, 33531733498 — each carrying complete per-leg TRX for all four hosted Windows shards and both hosted Linux shards, past the >=5-per-image bar in acceptance bullet 1. 18 artifacts (3 runs x 6 legs) were downloaded and both metrics quantified per image, with no local timings mixed in (bullet 3 honored).

**Summed TRX case duration is not reproducible per leg** — `windows-hosted-4-of-4`, identical class set, summed 1302.6s / 879.1s / 1447.6s across three runs (1.65x); `windows-hosted-1-of-4` summed 1075.2s / 1726.4s / 1329.3s (1.61x). The same-leg run-to-run swing exceeds the between-leg mean spread, so that metric **cannot drive a partition**.

**Wall time is already balanced.** Over the eight runs the four Windows legs mean 11.19 / 9.56 / 11.44 / 11.11 min against a per-run critical path averaging 12.05 min — a perfect duration partition recovers at most ~1.2 min (~10%), inside the ~+/-1.2 min single-leg noise band. Hosted Linux is balanced too (11.71 vs 10.74 min mean), and its residual gap is structural, not class skew: `linux-1-of-2` is the artifact owner and additionally runs docs validation (`ci.yml:183`), the format gate (`:219`), the NuGet audit (`:229`) and the publish upload (`:263`). The four-shard expansion recorded at `CI_POLICY.md:78` already absorbed the two-shard skew PR #1325 measured.

**So acceptance bullet 2's gate is NOT met — the correct outcome is "do not adopt".**

**Acceptance bullet 4 is already satisfied** for the static planner: `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs:60-90` asserts completeness, disjointness, non-empty shards and the greedy-balance bound across 1/2/4 shards; `:99-157` covers missing/unreadable/zero-class fail-closed; `:243-279` covers a stale external catalog in both drift directions.

**The one real remaining gap is DURABILITY.** `CI_POLICY.md:78` still tells a future reader to "rebalance only from repeated uploaded TRX evidence", but that evidence evaporates at 14 days, so the calibration is un-re-checkable and the next reader is pushed toward exactly the single-sample or local-timing mistake bullets 1 and 3 forbid.

**Re-scoped deliverable (replaces the original "balance the shards" framing):**
1. New `eng/collect-hosted-shard-timings.ps1` — offline, deterministic, takes a directory of downloaded `test-results-<leg>` folders plus leg metadata (leg, hosted image, wall-time seconds, run id). Mirror the TRX parsing in `eng/summarize-test-results.ps1:97` (`Read-TrxCases`) and `:185` (`Get-TimingAggregates -GroupBy Class`). Partition strictly by hosted image, refuse to merge images or accept an untagged leg, require `-MinimumSamples` (default 5) and fail closed below it. Emit wall-time skew and summed case duration as **separate** columns plus per-leg run-to-run spread and the achievable-gain figure, so a marginal skew cannot be misread as material. No `gh` call inside the script — keep it hermetic and off the CI critical path.
2. Rewrite the open "next calibration" sentence at `CI_POLICY.md:78` with the measured outcome, the ~1.2 min achievable-gain ceiling, the explicit decision that static discovered-case weights remain the fail-closed weighting, and the re-check trigger.
3. Record the collector in the evidence table at `ai_docs/references/testing.md:17-31`, plus a bullet stating that summed TRX case duration alone is not a partition signal on hosted runners.
4. Extend `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs` with deterministic collector regressions over TRX synthesized under `TestTempRoot.Current` (pattern at `TestShardPlanContractTests.cs:240-269`): under-minimum samples fails; two images in one profile fails; a run missing one leg of its image's set fails; a valid five-sample input reports both metrics separately and never merges images.

**Scope:** production 3 — `eng/collect-hosted-shard-timings.ps1` (new), `CI_POLICY.md`, `ai_docs/references/testing.md`. Tests 1 extended. `eng/get-test-shard-plan.ps1` is deliberately **unchanged**.

**Executor trap:** do NOT add a duration-profile parameter to `eng/get-test-shard-plan.ps1` plus a stale-profile test to satisfy bullets 2 and 4 literally — that ships an unused mechanism against measured evidence, and bullet 4 is already covered by existing tests.

**Scheduling:** `CI_POLICY.md` may also be touched by `ci-router-pure-decision` — do not co-schedule in one wave.
