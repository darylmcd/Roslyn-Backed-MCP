# linux-pwsh-startup-profile-write-ownership — Own shared native startup-profile writes

**row:** `linux-pwsh-startup-profile-write-ownership` · **pri:** `High` · **size:** `M`

## Anchors

- (new) eng/initialize-pwsh-startup-cache.sh
- .github/workflows/ci.yml (Linux validate job before first PowerShell step)
- .github/workflows/publish-nuget.yml (full-suite publish job before first PowerShell step)
- CI_POLICY.md
- (new) tests/RoslynMcp.Tests/LinuxPowerShellStartupCacheOwnershipTests.cs
- tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs
- tests/RoslynMcp.Tests/ManualNuGetPublishContractTests.cs

## Acceptance

- [ ] Observe actual old2374 nonroot valid-profile write access violate the required read-only consumption boundary; do not count a missing helper, textual mismatch or deliberately damaged input as the regression.
- [ ] Execute one shared bootstrap before first pwsh in both concurrent Linux full-suite owners: fresh native job-temporary XDG root/directories700, selected real binary single-writer valid seed via public StartProfile(null), checked/reaped seed, regular nonempty NonInteractive profile444 and writable containing directory. Export the same root to subsequent steps/descendants.
- [ ] Execute actual helper-driven native regression: profile write-open denied and hash unchanged across a fixed ordinary concurrent workload including File/Command/EncodedCommand and inherited child; preserve functional renderer/topology/aggregate assertions, ordinary sibling serialization/replacement, actual ModuleAnalysisCache persistence and existing telemetry configuration. No retries or synthetic profile bytes.
- [ ] Retain test coverage, launch arguments, parallelism, all deadlines, strict audit/format/release artifacts, NuGet cache policy and required-check semantics. Sequential roles and native SDK-floor remain unchanged where source proves no parallel PowerShell cohort.
- [ ] Run fresh implementation review, complete affected contract classes and actual native nonroot checks, actionlint/docs/fragments and all exact-head hosted checks before merge. Normal recording updates intentionally stop after the seed; reads and ordinary JIT remain. Do not claim adversarial immutability or all startup modes.
- [ ] Keep linux-pwsh-startup-profile-invalid-assembly-identity original causal-witness Acceptance OPEN and unchanged; successful owned-input prevention does not prove the observed CI134 provenance. No blocked fault-injection probe, internal runtime setting, ambient cache deletion, directory freeze, test weakening or retry-until-green.

## Evidence

- PR1849 head2374a09b run40373606428 Linux1 native134 with truncated assembly identity motivates this independent ownership correction; no original profile/crash artifact retained.
- Tagged runtime OpenForWrite uses CREATE_ALWAYS -> O_CREAT|O_TRUNC in-place; file0444 prevents ordinary recorder writes without blocking sibling storage. PowerShell reads XDG_CACHE_HOME before startup; setting it inside the later script is too late. Current launch modes select NonInteractive.
- Fixed legitimate nonrootext4 PowerShell7.6.6/.NET10.0.12 probe: both writable and file-only protected8+1functional cohorts pass; protected profile hash stable/write-open denied, sibling cache serialization works, actual telemetry.uuid present. Both normally generated profiles structurally valid; no crash or persisted corrupt identity reproduced. This is feasibility only, not required helper-driven red/green or module-analysis qualification.
- Cold plan APPROVE for bounded job-input ownership; original attribution objection remains. Precise plan and raw evidence retained in native-profile-ownership-plan-cold-verdict.md and native-cache-boundary-inventory.md in the delivery scratchpad. Code PR1849 retains zero backlog files; closure belongs only in trailing docs after actual merge.
