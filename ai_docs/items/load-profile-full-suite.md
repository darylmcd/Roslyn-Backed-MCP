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

2026-10-15 constructor full-gate diagnostic: a long quiet interval sampled ChangeSignaturePreview_ExternAliasSubstitution_PreservesAssembly(missing-directive-shadow-use) awaiting WorkspaceSessionLoader.CreateAndOpenAsync -> MSBuildWorkspace.OpenSolutionAsync -> RemoteBuildHost.LoadProjectFileAsync -> RpcClient.InvokeCoreAsync. Heap snapshot captured build-host PID50120; that PID was absent from a later native process inventory. The run subsequently resumed progress, with no additional failure reported at that point. This is a phase/lifetime witness, not proof of a crash or causal attribution to concurrent work. Reconcile final TRX durations before choosing a fix; do not weaken a deadline or label the event a flake.

TRX reconciliation for the same run: all324 ChangeSignaturePreviewTests cases passed. The sampled missing-directive-shadow-use case passed in2m53.488s, which includes the approximately3minute diagnostic dump pause. The later missing-PID inventory is not evidence of a crash or lost RPC response; do not treat that snapshot as a reproduced load defect. Full run:4433passed/7failed/19skipped,29m37s; five authored description assertions and two compile-readiness assertions. Dependency files changed during this already-red run, invalidating its restore baseline; no final-head success is claimed.
