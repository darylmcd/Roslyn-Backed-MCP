# CI Policy

This document is the canonical validation and merge-gating contract.

## Trigger And Routing Contract

| Event / trust state | Validation topology | Coverage / network |
|---|---|---|
| Evidence-only pull request (`ai_docs/audits/**`, `ai_docs/reports/**`, `ai_docs/items/**`, `audit-reports/**`, minus the exclusions below) | One pwsh `evidence-lint` job (changelog contract + `verify-ai-docs.ps1`); no .NET setup, build, or test legs; exact-floor probe and quality job skipped | Not applicable |
| Policy-only documentation pull request (Markdown / `ai_docs/**/*.json` / the tracked promotion scorecard, excluding behavior-bearing paths) | Three hosted Linux class shards plus the version-drift and breaking-version gates; exact-floor probe and quality job skipped | Coverage and `Network` tests skipped; publish/audit skipped |
| Any code-bearing pull request | Four hosted Windows shards + three hosted Linux shards + one concurrent exact-SDK-floor build/workspace probe + one concurrent quality job | Coverage and `Network` tests skipped |
| Merge-queue group (`merge_group`) | Always the full code-bearing topology (no trusted changed-file set, so no docs-only/evidence-only route) | Coverage and `Network` tests skipped |
| Manual dispatch / weekly schedule (Mon 05:45 UTC) | One unsharded hosted Linux suite + one concurrent exact-SDK-floor build/workspace probe + one concurrent quality job | Coverage and live `Network` tests enabled |

Rules:

- Do not add a push-to-`main` trigger. Protected changes arrive through an already-validated pull request.
- Cancel superseded pull-request runs. Never cancel dispatch or scheduled runs.
- Route all public-repository pull-request code only to GitHub-hosted runners. A repository-level self-hosted runner is not protected by an in-workflow fork predicate because a fork can modify the workflow itself.
- Keep the repository-level self-hosted runner inventory empty while this repository remains public under a personal account. Any future hybrid lane requires the infrastructure-enforced authorization and disposable-worker boundaries documented in `docs/self-hosted-runner.md` before registration.
- Classify both `filename` and `previous_filename` from the pull-request files API. A source-to-doc rename is code-bearing because its removed source path still requires full validation.
- Require the files API record count to equal `pull_request.changed_files` and remain below the endpoint's 3,000-file cap before selecting the docs-only route. A capped or incomplete enumeration is code-bearing and receives full validation.
- Treat behavior-bearing Markdown under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, and `.github/prompts/` as code-bearing. Those paths receive the full Windows/Linux matrix.
- Root `CHANGELOG.md` is docs tier. The docs route's artifact owner runs `verify-version-drift.ps1` and `verify-breaking-version-bump.ps1`, the gates `verify-release.ps1 -TestShardOnly` skips, so release version checks cannot be bypassed.
- A pull request takes the highest tier any of its paths (including rename origins) needs: code > docs > evidence.
- The evidence tier is limited to paths no test or `eng/` script reads. Files inside evidence roots that CI does read stay docs tier through `$evidenceExclusions` in `eng/resolve-ci-topology.ps1` (currently `ai_docs/items/backlog-d-fragment-schema.md` and `audit-reports/_latest-promotion-scorecard.json`). `CiEvidenceTierConsumerContractTests` fails on any new reference to an evidence root until it is classified.
- Never use workflow-level `paths`/`paths-ignore` to skip validation. The required `validate` check would never report and the pull request would stay blocked; skips must happen inside the workflow with `validate-gate` still reporting.
- On the evidence route `validate-gate` passes only when `evidence-lint` succeeded and the build/test matrix, SDK-floor probe, and quality job were skipped, never failed or cancelled.
- Keep the pull-request `validate-gate` check named `validate`. Dispatch/schedule runs must emit `validate-informational` so an informational one-leg run cannot satisfy the pull-request ruleset context.
- Merge-queue groups (`merge_group`) also report `validate`: the ruleset's merge queue waits on that context. The group is tested on the queued base plus every group ahead of it, which is the current-`main` integration check; do not duplicate it with a local full gate before enqueueing. The changed-file format gate diffs against `github.event.merge_group.base_sha`.

The routing decision itself -- leg construction, docs-only classification, and the fail-closed
count-mismatch/cap guard -- is a pure function in `eng/resolve-ci-topology.ps1`. The `route` job's
step performs the `gh api` pull-request file listing (the only GitHub API access in the decision
path) and hands the result to that script; it does not reimplement the decision inline. This keeps
the decision runnable and table-tested (`CiTopologyDecisionContractTests`) outside GitHub Actions.

The router emits typed leg fields. Keep their concerns separate:

| Field | Meaning |
|---|---|
| `runs_on` | GitHub-hosted runner label |
| `artifact_owner` | Sole policy/release-artifact owner |
| `timeout_minutes` | Explicit per-leg job cap |
| `test_shard_index`, `test_shard_count` | Complete class partition passed to `verify-release.ps1` |

## Pull-Request Test Partition

- Windows runs four concurrent class shards; Linux runs three (see § Linux Shard Count Decision).
- All seven code pull-request shards run on separate GitHub-hosted runners.
- Policy-only documentation PRs run the same complete class partition as three Linux shards. Do not restore a zero-test docs route: repository tests consume Markdown contracts, and future consumers must be covered automatically. The evidence route is the one zero-test route; it is safe only for paths `CiEvidenceTierConsumerContractTests` proves no test reads.
- `eng/get-test-shard-plan.ps1` reads compiled MSTest metadata. It must fail on an invalid index/count, missing assembly, unreadable metadata, unsafe class name, zero discovered classes, empty shard, overlap, or incomplete assignment.
- Shards use exact MSTest `ClassName` predicates. Do not use `FullyQualifiedName~` prefix matching.
- `eng/ci.runsettings` enables `TreatNoTestsAsError`; an accidentally empty filter is a failure.
- Keep pull-request coverage disabled. Keep dispatch/schedule coverage unsharded until coverage merge semantics are explicitly implemented.
- Keep matrix `fail-fast: false` so independent OS/shard failures remain visible in one run.
- Keep the exact SDK-floor lane isolated from hosted-image SDKs and bounded to restore, Release build, a representative MSBuildWorkspace load, and the native fork admission late-child metadata/copy cases. The latest-`10.0.x` matrix owns the complete suite.

## Required Checks And Artifacts

| Concern | Owner / condition |
|---|---|
| Release policy checks (version, skills, package allowlist, changelog, breaking version, registry readiness) | Artifact-owning code leg through the full release verifier |
| Changelog-fragment validation | Explicit artifact-owning policy-doc leg; otherwise included in the full release verifier |
| AI-doc and shipped-skill validation | Artifact-owning leg; includes policy-doc PRs |
| Restore, Release build, test | Every pull-request/test leg |
| Exact declared SDK floor | Concurrent `sdk-floor` job on code PRs, dispatch, and schedule; exact version assertion + restore/build + representative MSBuildWorkspace load + native fork admission late-child metadata/copy cases |
| Publish/hash | Artifact-owning non-doc leg |
| Fail-closed NuGet vulnerability audit | Concurrent route-dependent Linux quality job on code PRs, merge groups, dispatch, and schedule; independent of test shards and SDK floor |
| Changed-file formatter gate | Quality job on code PRs and merge groups, after audit restore and a checked Debug build of the repository analyzer; new FINALNEWLINE, IDE1006, IMPORTS, and WHITESPACE findings on touched files fail, tracked baseline debt is reported not suppressed |
| `host-stdio-publish`, `release-manifests` | Artifact-owning non-doc leg; 14 days |
| Per-leg timing summary + TRX (`test-results-<leg>`) | Every pull-request/test leg, generated/uploaded with `always()`; 14 days |
| Cobertura + HTML `code-coverage` | Dispatch/schedule artifact owner; 30 days |

`eng/verify-release.ps1` remains a standalone release gate. It calls `eng/prepare-validation-dependencies.ps1` for checked main restore plus canonical all-sample preparation, verifies restored root-solution licenses on non-test-only legs, builds Release, runs tests in a private per-invocation `TEMP`/`TMP`/`TMPDIR`, disables reusable MSBuild and compiler servers within the invocation, removes the exact temp root, publishes the stdio host, and writes the hash manifest. The aggregate preparation script composes `eng/prepare-test-fixtures.ps1`, the sole sample-discovery owner also used by direct tests and `just test`. Restored-license verification retains the root solution and active package-root filters; sample assets do not replace root metadata. Preparation or license failure prevents build, tests, and publication. Test and cleanup failures are aggregated rather than hidden. CI passes `-TestShardOnly` on non-owner code legs and both policy-doc legs, so platform-neutral policy and publish/hash work execute once where required; the default remains the complete standalone gate.

The quality job has a 30-minute cap, full-history checkout, and latest-`10.0.x` SDK. It runs the mandatory audit first to restore its own solution assets. On an exact cache miss it also prepares all canonical samples and explicitly saves packages before remaining quality work. Code PRs and merge groups then build `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.csproj` in Debug with checked native exit before changed-file formatting with `-NoRestore`; restore alone does not produce the project analyzer binary. Dispatch/schedule retain the mandatory audit and conditional sample preparation/cache save. Policy-doc and evidence routes skip the job.

## Linux PowerShell Startup Input Ownership

- Linux `validate` legs and the NuGet `publish` job run `eng/initialize-pwsh-startup-cache.sh` before their first PowerShell step. One normal seed records a fresh job-private `StartupProfileData-NonInteractive`; subsequent ordinary writes to that file are denied (mode444). Its private cache directory stays writable (mode700) for module analysis, telemetry under existing configuration and other siblings.
- The helper persists the unique native `RUNNER_TEMP` cache root through `GITHUB_ENV`; unchanged test launchers inherit it. Normal JIT and every functional test remain enabled. Post-seed optional startup recording is intentionally denied; this ownership boundary does not establish the cause or cure of any prior native crash. Runner teardown owns cleanup after descendants finish.
- Sequential routing, quality, evidence, aggregate and SDK-floor jobs retain their existing lifetime. Local operator runs are separate from this hosted-job boundary.

## NuGet Cache Lifecycle

- Both cache sites use pinned restore-only and explicit save-only actions; no matrix reader registers post-test cache compression.
- Restore/save share `NUGET_CACHE_PATH`, the existing OS-qualified dependency hash key and fallback prefix. Save uses the restore action's primary-key output, never a recomputed key.
- Save only after successful population when `cache-hit != 'true'`; exact hits and readers never save. Prefix hits and cold misses retain population and persistence.
- Linux code PRs, merge groups, dispatch and schedule: quality is the sole writer, after strict audited main restore plus canonical sample preparation and before analyzer/formatter work.
- Windows code PRs and merge groups: shard index 0 is the sole writer. Policy-doc Linux: the artifact owner is the sole writer. Each calls the same main-plus-sample preparation as normal release verification and saves before either release step.
- Every release leg still performs checked preparation, even on cache hits. Cache state never substitutes for restore, audit, build, tests or release gates. Evidence-only and SDK-floor routes retain their existing uncached behavior.
- Preserve native immutable-cache races across separate runs. Readers may restore before the writer saves. Lookup remains OS/version and current/default/applicable-base-ref scoped; PR-created caches remain PR-ref scoped and are not promoted to main. Dispatch/schedule on main can populate the default-branch Linux cache.
- Save work still consumes its writer's unchanged job budget. A warm hit proves no cold save; inspect actual key, matched key, ref, OS, explicit save and cleanup logs for cold-save qualification. Linux observations do not prove Windows persistence. Fresh five-complete-green-run timing calibration remains required before claiming a steady-state improvement.

CI and `just vuln-audit` invoke `eng/verify-nuget-audit.ps1`. It forces restore with `NuGetAuditMode=all` and promotes NU1900-NU1904 to errors, covering advisory-source failure plus vulnerable direct/transitive packages.

## Pre-Sharding Timing Evidence (historical)

Superseded by the 2026-10-29 recheck under § Hosted Shard Weighting Decision. Every number in this section predates sharding and the suite growth since; none describes the current steady state.

Measured from the five most recent successful PR legs before this refactor:

| Metric | Repository-level Windows | Hosted Linux |
|---|---:|---:|
| Median job | 16m09s | 25m10s |
| Median `verify-release` | 15m42s | 24m33s |
| Median `dotnet test` | 15m23s | 23m40s |
| Tests as release-step share | about 98% | about 97% |

Fresh Windows TRX baseline: 2,536 cases (2,530 passed, 6 skipped) in 16m30s. MSTest spent about 6m13s in the parallelizable phase and 10m17s in the serialized `[DoNotParallelize]` tail. The same run proved that scripting watchdog tests left eight CPU-bound background threads alive for the remainder of the testhost. Treat this baseline as pre-refactor evidence; use uploaded per-leg TRX from the merged workflow for the new steady state.

Wave 40 local Windows timing probe (2026-09-29): repeat the command below three times on each side of removing `WorkspaceValidationTimeoutTests`'s opt-out, changing only the TRX filename per run. All six two-class TRX files recorded 25 passed, 0 failed. Durations below are from TRX test start/end timestamps, not the `dotnet test` process wall time.

```powershell
$probeOutput = Join-Path ([IO.Path]::GetTempPath()) 'roslyn-wave40-evidence'
New-Item -ItemType Directory -Force -Path $probeOutput | Out-Null
dotnet test tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj --no-restore --settings eng/ci.runsettings --filter 'ClassName=RoslynMcp.Tests.WorkspaceToolsIntegrationTests|ClassName=RoslynMcp.Tests.WorkspaceValidationTimeoutTests' --results-directory $probeOutput --logger 'trx;LogFileName=wave40-before-1.trx'
```

| Local probe | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| Before: both classes in serialized tail | 23.0 s | 20.2 s | 14.1 s |
| After: retained `WorkspaceToolsIntegrationTests` serialized tail | 6.7 s | 6.7 s | 6.6 s |
| After: parallel-eligible `WorkspaceValidationTimeoutTests` | 7.2 s | 6.9 s | 7.0 s |

An additional three-class probe appended `|ClassName=RoslynMcp.Tests.StringLiteralReplaceServiceTests` to the filter after removal. Each of its three TRX files recorded 27 passed, 0 failed; the timeout class overlapped that parallel-enabled companion for 4.0 s, 4.5 s, and 4.2 s, respectively. The retained shared-workspace class ran alone in the serialized tail for 6.3 s, 8.2 s, and 6.1 s. These bounded local runs establish the class-level ownership change; they do not replace a full-suite or hosted-runner baseline.

The first hosted two-shard calibration completed Windows in 19m50s and 16m29s, with cumulative per-test TRX durations of 2,549s and 1,764s. Because that did not improve the prior 16m09s repository-level median, code pull requests use four hosted Windows shards.

GitHub's public-repository `ubuntu-latest` and `windows-latest` standard runners currently provide 4 CPUs and 16 GB RAM. Do not retain the retired 2-vCPU assumption in repository documentation.

## Hosted Shard Weighting Decision

The four-shard calibration is closed. `eng/collect-hosted-shard-timings.ps1` measured five complete green `ci` runs (30 leg observations), modelling each hosted image independently and mixing in no local-machine timing:

| Hosted image | Legs | Critical path | Balanced floor | Achievable gain | Single-leg noise band | Worst same-leg case-duration spread | Between-leg case-duration spread |
|---|---:|---:|---:|---:|---:|---:|---:|
| `windows-latest` | 4 | 720.4s | 647.1s | 73.4s (10.2%) | 82.9s | 1.87x | 1.50x |
| `ubuntu-latest` | 2 | 743.2s | 662.6s | 80.6s (10.8%) | 140.3s | 2.21x | 1.18x |

Superseded by the 2026-10-29 recheck below. Decision at that calibration: the deterministic discovered-case weights in `eng/get-test-shard-plan.ps1` remain the shard weighting on both hosted images. Do not add an OS-specific duration profile. A perfect duration partition recovers less than the single-leg wall-time noise band on either image, and summed TRX case duration is not a partition signal at all: its same-leg run-to-run swing exceeds the between-leg spread, so ranking legs by it reproduces runner noise rather than class cost. At that calibration, the residual `ubuntu-latest` gap was structural rather than class skew: the artifact-owner leg also ran docs validation, formatting, the NuGet audit, and publish upload. Formatting and audit now run in the independent quality job; docs validation and publish remain with the artifact owner. This movement triggers a fresh five-complete-green-run timing recheck; the historical measurements above do not establish the new steady state or a numerical improvement.

### 2026-10-29 recheck (seven-leg topology)

Trigger met: the Linux shard count changed (2 to 3) and non-test work moved to the quality job. `eng/collect-hosted-shard-timings.ps1` measured five complete green first-attempt `ci` pull-request runs (35 leg observations; 4 Windows + 3 Linux legs each), TRX downloaded inside the 14-day window, no local timing mixed in:

| Run | Head | Created (UTC) |
|---|---|---|
| 40783926346 | b74f0546 | 2026-10-28 22:58 |
| 40788319028 | e24d0063 | 2026-10-28 23:34 |
| 40788434338 | a92e1033 | 2026-10-28 23:35 |
| 40800095395 | a7cf6874 | 2026-10-29 01:23 |
| 40803366211 | 35477746 | 2026-10-29 01:56 |

Image evidence is the `windows-latest` / `ubuntu-latest` job label only; runner image build versions were not captured. The five heads are different pull requests (cases per Windows leg ranged 1358-1428), so the sample mixes suite revisions.

| Hosted image | Legs | Critical path | Balanced floor | Achievable gain | Single-leg noise band | Worst same-leg case-duration spread | Between-leg case-duration spread |
|---|---:|---:|---:|---:|---:|---:|---:|
| `windows-latest` | 4 | 1827.2s | 1348.9s | 478.4s (26.2%) | 325.9s | 1.93x | 2.09x |
| `ubuntu-latest` | 3 | 1322.8s | 1093.8s | 229.0s (17.3%) | 224.2s | 1.68x | 1.98x |

Findings:

- The collector reports material skew on both images: the achievable gain exceeds the single-leg noise band, and summed case duration ranks legs (between-leg spread above same-leg spread, narrowly on Windows). Under the re-check trigger above, the case-count weighting no longer stands. An image-specific duration profile is warranted for both images; implementation is tracked in `ci-shard-duration-profile-hosted`, not done by this recheck.
- Static case counts misjudge cost. Mean TRX case seconds per run on `windows-latest`: `InterfaceBaseTypeInsertionIntegrationTests` 1013s (12 test methods in the file, so about 85s each), `ForkNativeAdmissionTests` 759s, `MovedDeclarationFileOwnershipIntegrationTests` 569s, `CrossProjectReceivingNamespaceIntegrationTests` 511s. The same four lead `ubuntu-latest`. MSTest runs a class's cases serially, so one such class floors its leg's wall time; a partition cannot beat the largest class. Cost reduction for these classes is tracked in `test-heavy-integration-class-cost` and its `test-heavy-class-cost-*` siblings.
- The Windows critical path moved from 720.4s (earlier calibration, about 2,500 total cases) to 1827.2s with about 5,600 cases across the four legs: the growth is mostly suite size, not shard topology.
- `windows-hosted-1-of-4` (1621.8s mean) and `windows-hosted-4-of-4` (1646.2s mean) bound the critical path; `windows-hosted-2-of-4` averages 860.6s.

Evidence limits: five runs across five heads, one image version unknown, per-leg wall spread 1.4x-1.9x. Re-run the collector after the profile lands; do not tune a profile from fewer than five complete green runs.

## Linux Shard Count Decision

Operator decision 2026-10-26 supersedes the two-Linux-shard topology for code and policy-only documentation PRs. Both routes now run three `ubuntu-latest` class shards; the last shard stays the sole artifact owner, and the 30-minute leg limit is unchanged.

| Evidence (hosted, before this change) | Linux leg wall time |
|---|---|
| Code run 40401658309 | linux-1 27m45 / linux-2 22m |
| Code run 40419476050 (#1855) | linux-1 27m51 / linux-2 17m |
| Code run 40412863293 (#1854) | linux-1 cancelled at 30m after 2,660 passed, 0 failed (28m23 of tests) |
| Docs runs for #1853 / #1848 | 22m–27m27 per leg |

The complete Linux partition no longer fits two legs with margin: ordinary test growth reaches the deadline. A third shard keeps the static case-count weighting above (no duration profile), preserves the complete disjoint partition, fail-closed planning, deadlines and single-owner artifact/cache rules, and adds one hosted runner per Linux route. The shard-count change fired the re-check trigger below; the 2026-10-29 recheck under § Hosted Shard Weighting Decision measured it.

Re-check trigger: re-run `eng/collect-hosted-shard-timings.ps1` when the shard count changes, when non-test work moves between legs, or when a hosted image's achievable gain exceeds its own single-leg noise band across at least five complete green runs. Uploaded per-leg TRX expires after 14 days, so always re-derive from fresh in-window runs. Never calibrate from a single sample, and never feed a local Windows profile into the hosted `windows-latest` profile.

## Test Execution Contract

- Assembly default: `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]`.
- Apply `[DoNotParallelize]` only for a documented process-global/shared-workspace dependency. An isolated per-class workspace is not, by itself, justification for serialization.
- Platform-specific tests must report `Assert.Inconclusive` (or an equivalent explicit skip) on unsupported operating systems. A bare early `return` is a false pass.
- Tests that abandon non-cooperative work must release it before returning or isolate it in a disposable child process. Never contaminate the shared testhost with a permanent busy thread.
- Preserve distinct behavioral cases even when setup/shape is structurally similar. Remove only genuine same-entry-point, same-input, same-assertion duplication.
- Treat TRX duration as evidence; method counts and timeout ceilings are prioritization signals only.
- Keep `eng/summarize-test-results.ps1` path-safe: job summaries may contain bounded class/method names and timings, never TRX `codeBase` or source-path metadata.

## Test Category Lanes

| Lane | Meaning | Default CI behavior | Sample filter |
|---|---|---|---|
| `Benchmark` | Opt-in wall-clock benchmark | Always excluded | `TestCategory=Benchmark` |
| `Network` | Live external-service calls | Excluded on PR; included weekly/manual | `TestCategory!=Network` |
| `RepoSolution` | Loads this repository solution / another large workspace | Included | `TestCategory!=RepoSolution` |
| `Process` | Starts an external process | Included | `TestCategory!=Process` |
| `Performance` | Wall-clock budget assertions | Included | `TestCategory!=Performance` |

Combine filters with `&` (AND) or `|` (OR) per `dotnet test` syntax.

## Local Validation

| Scope | Command |
|---|---|
| AI-doc changes | `pwsh -NoProfile -File ./eng/verify-ai-docs.ps1` |
| Required PR-equivalent gate | `just ci` |
| Formatter debt on touched C# files | `pwsh -NoProfile -File ./eng/verify-changed-format.ps1` |
| GitHub Actions workflow YAML/expression lint | `pwsh -NoProfile -File ./eng/verify-actionlint.ps1` |
| Informational coverage/live-network gate | `just full` |
| One release shard | `pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -TestShardIndex <zero-based> -TestShardCount <count>` |

`just ci` composes docs, shipped skills, the changed-file formatter gate, the checksum-pinned actionlint gate, unsharded PR-equivalent release validation, and the vulnerability audit. The same actionlint script also runs on the hosted artifact-owner PR leg. Local validation remains unsharded so one command proves the complete suite.

## Merge Gating Expectations

- Do not declare merge-ready while any required validation leg is failing, cancelled, skipped, or pending.
- `validate-gate` depends on `route`, the complete `validate` matrix, `sdk_floor`, `quality`, and `evidence_lint`; it reports `validate` only when routing and every job required by that route succeeded. Code routes require matrix, SDK floor, and quality success with evidence lint skipped. Policy-doc routes require matrix success with floor, quality, and evidence lint skipped. Evidence routes require evidence lint success with matrix, floor, and quality skipped. Missing or unexpected results fail closed.
- Dispatch/schedule runs report `validate-informational`, never the required pull-request context.
- The standalone `# ci-backstop: validation-only` declaration in `ci.yml` authorizes the shipping helper to dispatch the existing manual validation workflow on the merged base. That run adds unsharded Linux coverage, live-network canaries, and the SDK-floor probe after the reviewed PR gate; its `validate-informational` result cannot satisfy required `validate` or authorize release/package publication, deployment, or SDK generation.
- Do not pin dynamic per-leg names in branch protection.
- Preserve Linux pre-merge coverage because NuGet publication validates on Linux; this closes the prior Windows-pass/Linux-publish-fail gap.
- Synchronize the branch before merge when repository protection requires it.

## Other Repository Evidence

- CodeQL uses GitHub default setup for C# and GitHub Actions (`Analyze (csharp)`, `Analyze (actions)`). Do not add a duplicate advanced workflow while default setup is active.
- NuGet caching keys on `Directory.Packages.props`, `Directory.Build.props`, and `global.json`.

## Ownership

- Workflow implementation: `.github/workflows/ci.yml`
- Validation-topology decision (pure function, table-tested): `eng/resolve-ci-topology.ps1`
- Human self-hosted operations: `docs/self-hosted-runner.md`
- Runtime and local commands: `ai_docs/runtime.md`
- Branch/worktree/PR workflow: `ai_docs/workflow.md`
