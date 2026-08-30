# release-cut-step3-environment-precheck — Step 3 runs the heavy gate with no environment guard

**row:** `release-cut-step3-environment-precheck` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.claude/skills/release-cut/SKILL.md` — Step 3 Verify
- `eng/verify-release.ps1` — entry point Step 3 invokes

## Acceptance

- [ ] Step 3 performs a cheap environment pre-check before invoking `verify-release.ps1`: available physical memory and count of stray `dotnet`/`testhost`/`MSBuild`/`VBCSCompiler` processes.
- [ ] Below a stated threshold the skill refuses with a corrective (drain build servers, kill strays, re-check) rather than running a gate whose result cannot be trusted.
- [ ] Step 3 states that a red verify must be triaged against machine state **before** it is treated as a product failure, alongside the existing collector-crash caveat.
- [ ] A killed or timed-out verify run is followed by an explicit cleanup step, so leaked hosts cannot poison the next attempt.

## Evidence

- 2026-08-28 release cut: Step 3 returned exit 1 with an `AccessViolationException` in analyzer reflection. Root-caused across ~40 minutes to a product defect that did not exist — the box had 1.9 GB free of 31.4 GB and 31 leaked `dotnet`-family processes. After cleanup the same suite passed 2726/2726 twice (19m37s, then 16m06s on the release commit). Killed retries left further orphaned hosts behind; whether they measurably worsened each subsequent run was not instrumented.

## Context

Step 3 documents one false-red mode (the Coverlet `-NoCoverage` teardown crash) but nothing about environment. A memory-exhausted box produces exactly the failure signature that reads as a serious product defect: `AccessViolationException` during assembly loading, plus teardown hangs from `GC.Collect()` / `WaitForPendingFinalizers()` under pressure.

The trap is that the signature is expensive to disbelieve. It looks like a real crash with a real stack through real product code, so the natural response is a root-cause investigation rather than a machine-state check.

## Notes

- The check is cheap: free-memory and a process count, seconds at most, against a gate that costs ~20 minutes.
- Failure/kill cleanup matters as much as the pre-check: killed runs leave orphaned test hosts and build servers that the next attempt inherits.
- Related to `ci-merge-gate-publish-gate-shape-divergence`: a green unsharded CI leg would give an independent signal to check a local red against.
2026-08-30 validation evidence: the first monolithic release test run passed 2,050 executed tests, then aborted with AccessViolationException in Roslyn completion-provider discovery while 11 orphaned MSBuild nodes remained. After targeted orphan cleanup, a second monolithic run hung for 163 minutes in Roslyn workspace checksum/file loading while an unrelated repository testhost consumed about 2.5 GB; a live mini-dump confirmed the wait outside the changed diagnostic paths. The same final tree then passed all four canonical shards serially (2,764 passed, 7 expected platform skips, 2,771 total) with orderly server shutdown after every shard. This strengthens the need for the row's precheck, bounded-hang diagnostics, and failure cleanup acceptance.
2026-08-30 correction after PR #1402: a clean GitHub-hosted Windows 4-of-4 runner reproduced the same RuntimeType.IsAssignableFrom access violation in ServiceCoverageTests.GetCompletions_Returns_Items_At_Valid_Position after 586 passing cases. The signature alone is therefore not proof of machine pressure. Root cause was the product's over-broad analyzer isolation retargeting immutable SDK/NuGet analyzers into collectible load contexts later traversed by Roslyn completion-provider caches; tracked and fixed as analyzer-shadow-loader-external-scope-crash. This row still applies to genuine low-memory/orphan-host conditions, but its precheck guidance must distinguish those measured conditions from this now-fixed product defect.
