# CI Policy

This document is the canonical validation and merge-gating contract.

## Trigger And Routing Contract

| Event / trust state | Validation topology | Coverage / network |
|---|---|---|
| Policy-only documentation pull request (Markdown / `ai_docs/**/*.json`, excluding behavior-bearing paths) | Two hosted Linux class shards; exact-floor probe skipped | Coverage and `Network` tests skipped; publish/audit skipped |
| Any code-bearing pull request | Four hosted Windows shards + two hosted Linux shards + one concurrent exact-SDK-floor build/workspace probe | Coverage and `Network` tests skipped |
| Manual dispatch / weekly schedule (Mon 05:45 UTC) | One unsharded hosted Linux suite + one concurrent exact-SDK-floor build/workspace probe | Coverage and live `Network` tests enabled |

Rules:

- Do not add a push-to-`main` trigger. Protected changes arrive through an already-validated pull request.
- Cancel superseded pull-request runs. Never cancel dispatch or scheduled runs.
- Route all public-repository pull-request code only to GitHub-hosted runners. A repository-level self-hosted runner is not protected by an in-workflow fork predicate because a fork can modify the workflow itself.
- Keep the repository-level self-hosted runner inventory empty while this repository remains public under a personal account. Any future hybrid lane requires the infrastructure-enforced authorization and disposable-worker boundaries documented in `docs/self-hosted-runner.md` before registration.
- Classify both `filename` and `previous_filename` from the pull-request files API. A source-to-doc rename is code-bearing because its removed source path still requires full validation.
- Require the files API record count to equal `pull_request.changed_files` and remain below the endpoint's 3,000-file cap before selecting the docs-only route. A capped or incomplete enumeration is code-bearing and receives full validation.
- Treat root `CHANGELOG.md` and behavior-bearing Markdown under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, and `.github/prompts/` as code-bearing. Those paths receive the full Windows/Linux matrix. Release assembly/version checks therefore cannot be bypassed by the policy-doc route.
- Keep the pull-request `validate-gate` check named `validate`. Dispatch/schedule runs must emit `validate-informational` so an informational one-leg run cannot satisfy the pull-request ruleset context.

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

- Windows runs four concurrent class shards; Linux runs two.
- All six code pull-request shards run on separate GitHub-hosted runners.
- Policy-only documentation PRs run the same complete class partition as two Linux shards. Do not restore a zero-test docs route: repository tests consume Markdown contracts, and future consumers must be covered automatically.
- `eng/get-test-shard-plan.ps1` reads compiled MSTest metadata. It must fail on an invalid index/count, missing assembly, unreadable metadata, unsafe class name, zero discovered classes, empty shard, overlap, or incomplete assignment.
- Shards use exact MSTest `ClassName` predicates. Do not use `FullyQualifiedName~` prefix matching.
- `eng/ci.runsettings` enables `TreatNoTestsAsError`; an accidentally empty filter is a failure.
- Keep pull-request coverage disabled. Keep dispatch/schedule coverage unsharded until coverage merge semantics are explicitly implemented.
- Keep matrix `fail-fast: false` so independent OS/shard failures remain visible in one run.
- Keep the exact SDK-floor lane isolated from hosted-image SDKs and bounded to restore, Release build, and one real MSBuildWorkspace load. The latest-`10.0.x` matrix owns the complete suite.

## Required Checks And Artifacts

| Concern | Owner / condition |
|---|---|
| Release policy checks (version, skills, package allowlist, changelog, breaking version, registry readiness) | Artifact-owning code leg through the full release verifier |
| Changelog-fragment validation | Explicit artifact-owning policy-doc leg; otherwise included in the full release verifier |
| AI-doc and shipped-skill validation | Artifact-owning leg; includes policy-doc PRs |
| Restore, Release build, test | Every pull-request/test leg |
| Exact declared SDK floor | Concurrent `sdk-floor` job on code PRs, dispatch, and schedule; exact version assertion + restore/build + representative MSBuildWorkspace load |
| Publish/hash | Artifact-owning non-doc leg |
| Fail-closed NuGet vulnerability audit | Artifact-owning non-doc leg |
| Changed-file formatter gate | Artifact-owning non-doc pull-request leg; new FINALNEWLINE, IDE1006, IMPORTS, and WHITESPACE findings on touched files fail, tracked baseline debt is reported not suppressed |
| `host-stdio-publish`, `release-manifests` | Artifact-owning non-doc leg; 14 days |
| Per-leg timing summary + TRX (`test-results-<leg>`) | Every pull-request/test leg, generated/uploaded with `always()`; 14 days |
| Cobertura + HTML `code-coverage` | Dispatch/schedule artifact owner; 30 days |

`eng/verify-release.ps1` remains a standalone release gate. It restores the main and sample solutions, builds Release, runs tests in a private per-invocation `TEMP`/`TMP`/`TMPDIR`, shuts down child build servers, removes the exact temp root, publishes the stdio host, and writes the hash manifest. Test and cleanup failures are aggregated rather than hidden. CI passes `-TestShardOnly` on non-owner code legs and both policy-doc legs, so platform-neutral policy and publish/hash work execute once where required; the default remains the complete standalone gate.

CI and `just vuln-audit` invoke `eng/verify-nuget-audit.ps1`. It forces restore with `NuGetAuditMode=all` and promotes NU1900-NU1904 to errors, covering advisory-source failure plus vulnerable direct/transitive packages.

## Current Timing Evidence

Measured from the five most recent successful PR legs before this refactor:

| Metric | Repository-level Windows | Hosted Linux |
|---|---:|---:|
| Median job | 16m09s | 25m10s |
| Median `verify-release` | 15m42s | 24m33s |
| Median `dotnet test` | 15m23s | 23m40s |
| Tests as release-step share | about 98% | about 97% |

Fresh Windows TRX baseline: 2,536 cases (2,530 passed, 6 skipped) in 16m30s. MSTest spent about 6m13s in the parallelizable phase and 10m17s in the serialized `[DoNotParallelize]` tail. The same run proved that scripting watchdog tests left eight CPU-bound background threads alive for the remainder of the testhost. Treat this baseline as pre-refactor evidence; use uploaded per-leg TRX from the merged workflow for the new steady state.

The first hosted two-shard calibration completed Windows in 19m50s and 16m29s, with cumulative per-test TRX durations of 2,549s and 1,764s. Because that did not improve the prior 16m09s repository-level median, code pull requests use four hosted Windows shards.

GitHub's public-repository `ubuntu-latest` and `windows-latest` standard runners currently provide 4 CPUs and 16 GB RAM. Do not retain the retired 2-vCPU assumption in repository documentation.

## Hosted Shard Weighting Decision

The four-shard calibration is closed. `eng/collect-hosted-shard-timings.ps1` measured five complete green `ci` runs (30 leg observations), modelling each hosted image independently and mixing in no local-machine timing:

| Hosted image | Legs | Critical path | Balanced floor | Achievable gain | Single-leg noise band | Worst same-leg case-duration spread | Between-leg case-duration spread |
|---|---:|---:|---:|---:|---:|---:|---:|
| `windows-latest` | 4 | 720.4s | 647.1s | 73.4s (10.2%) | 82.9s | 1.87x | 1.50x |
| `ubuntu-latest` | 2 | 743.2s | 662.6s | 80.6s (10.8%) | 140.3s | 2.21x | 1.18x |

Decision: the deterministic discovered-case weights in `eng/get-test-shard-plan.ps1` remain the shard weighting on both hosted images. Do not add an OS-specific duration profile. A perfect duration partition recovers less than the single-leg wall-time noise band on either image, and summed TRX case duration is not a partition signal at all: its same-leg run-to-run swing exceeds the between-leg spread, so ranking legs by it reproduces runner noise rather than class cost. The residual `ubuntu-latest` gap is structural rather than class skew, because the artifact-owner leg also runs docs validation, the format gate, the NuGet audit, and the publish upload.

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
| Informational coverage/live-network gate | `just full` |
| One release shard | `pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -TestShardIndex <zero-based> -TestShardCount <count>` |

`just ci` composes docs, shipped skills, the changed-file formatter gate, unsharded PR-equivalent release validation, and the vulnerability audit. Local validation remains unsharded so one command proves the complete suite.

## Merge Gating Expectations

- Do not declare merge-ready while any required validation leg is failing, cancelled, skipped, or pending.
- `validate-gate` depends on `route`, the complete `validate` matrix, and `sdk-floor`; it reports `validate` only when all required jobs succeeded (the floor job is intentionally skipped for policy-only docs).
- Dispatch/schedule runs report `validate-informational`, never the required pull-request context.
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
