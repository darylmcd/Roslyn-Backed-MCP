# load-profile-full-suite — Profile full-suite load and decide the test worker cap

**row:** `load-profile-full-suite` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/AssemblyInfo.cs:9`
- `eng/ci.runsettings`
- `eng/verify-release.ps1`

## Acceptance

- [ ] Capture thread-pool queue length, worker count and machine CPU during a `verify-release` run, locally and on the hosted `windows-hosted-*` shards, and name the test classes that saturate the machine.
- [ ] Decide with that evidence whether to cap `Workers` (currently `Workers = 0`, ProcessorCount, at `AssemblyInfo.cs:9`; `eng/ci.runsettings` has no cap) or mark the saturating classes `[DoNotParallelize]`; apply it and show the timing-test failures stop under the full suite.

## Evidence

- Plan 20260930T213336Z: four tests failed intermittently only under the full parallel suite (retirement retry timeout, shadow-sweep PowerShell start, atomic-writer retry bound, warm-ratio assertion) after the 2026-09-29/30 parallelization changes (DoNotParallelize audit waves, PR 1684).

## Context

- Split child of `load-sensitive-timing-tests-full-suite` (plan 20261001T034545Z shipped its deterministic per-test subset). The capture needs a human-supervised or long-running run; no headless executor shortcut exists.
