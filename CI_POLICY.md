# CI Policy

This document is the canonical validation and merge-gating contract.

## Trigger And Routing Contract

| Event / trust state | Validation topology | Coverage / network |
|---|---|---|
| Policy-only documentation pull request (Markdown / `ai_docs/**/*.json`, excluding behavior-bearing paths) | Two hosted Linux class shards | Coverage and `Network` tests skipped; publish/audit skipped |
| Any code-bearing pull request | Four hosted Windows shards + two hosted Linux shards | Coverage and `Network` tests skipped |
| Manual dispatch / weekly schedule (Mon 05:45 UTC) | One unsharded hosted Linux suite | Coverage and live `Network` tests enabled |

Rules:

- Do not add a push-to-`main` trigger. Protected changes arrive through an already-validated pull request.
- Cancel superseded pull-request runs. Never cancel dispatch or scheduled runs.
- Route all public-repository pull-request code only to GitHub-hosted runners. A repository-level self-hosted runner is not protected by an in-workflow fork predicate because a fork can modify the workflow itself.
- Keep the repository-level self-hosted runner inventory empty while this repository remains public under a personal account. Any future hybrid lane requires the infrastructure-enforced authorization and disposable-worker boundaries documented in `docs/self-hosted-runner.md` before registration.
- Classify both `filename` and `previous_filename` from the pull-request files API. A source-to-doc rename is code-bearing because its removed source path still requires full validation.
- Require the files API record count to equal `pull_request.changed_files` and remain below the endpoint's 3,000-file cap before selecting the docs-only route. A capped or incomplete enumeration is code-bearing and receives full validation.
- Treat root `CHANGELOG.md` and behavior-bearing Markdown under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, and `.github/prompts/` as code-bearing. Those paths receive the full Windows/Linux matrix. Release assembly/version checks therefore cannot be bypassed by the policy-doc route.
- Keep the pull-request `validate-gate` check named `validate`. Dispatch/schedule runs must emit `validate-informational` so an informational one-leg run cannot satisfy the pull-request ruleset context.

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

## Required Checks And Artifacts

| Concern | Owner / condition |
|---|---|
| Release policy checks (version, skills, package allowlist, changelog, breaking version, registry readiness) | Artifact-owning code leg through the full release verifier |
| Changelog-fragment validation | Explicit artifact-owning policy-doc leg; otherwise included in the full release verifier |
| AI-doc and shipped-skill validation | Artifact-owning leg; includes policy-doc PRs |
| Restore, Release build, test | Every pull-request/test leg |
| Publish/hash | Artifact-owning non-doc leg |
| Fail-closed NuGet vulnerability audit | Artifact-owning non-doc leg |
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

The first hosted two-shard calibration completed Windows in 19m50s and 16m29s, with cumulative per-test TRX durations of 2,549s and 1,764s. Because that did not improve the prior 16m09s repository-level median, code pull requests use four hosted Windows shards. Use complete green four-shard hosted runs as the next calibration and rebalance only from repeated uploaded TRX evidence.

GitHub's public-repository `ubuntu-latest` and `windows-latest` standard runners currently provide 4 CPUs and 16 GB RAM. Do not retain the retired 2-vCPU assumption in repository documentation.

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
| Informational coverage/live-network gate | `just full` |
| One release shard | `pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -TestShardIndex <zero-based> -TestShardCount <count>` |

`just ci` composes docs, shipped skills, unsharded PR-equivalent release validation, and the vulnerability audit. Local validation remains unsharded so one command proves the complete suite.

## Merge Gating Expectations

- Do not declare merge-ready while any required validation leg is failing, cancelled, skipped, or pending.
- `validate-gate` depends on both `route` and the complete `validate` matrix and reports `validate` only when both succeeded.
- Dispatch/schedule runs report `validate-informational`, never the required pull-request context.
- Do not pin dynamic per-leg names in branch protection.
- Preserve Linux pre-merge coverage because NuGet publication validates on Linux; this closes the prior Windows-pass/Linux-publish-fail gap.
- Synchronize the branch before merge when repository protection requires it.

## Other Repository Evidence

- CodeQL uses GitHub default setup for C# and GitHub Actions (`Analyze (csharp)`, `Analyze (actions)`). Do not add a duplicate advanced workflow while default setup is active.
- NuGet caching keys on `Directory.Packages.props`, `Directory.Build.props`, and `global.json`.

## Ownership

- Workflow implementation: `.github/workflows/ci.yml`
- Human self-hosted operations: `docs/self-hosted-runner.md`
- Runtime and local commands: `ai_docs/runtime.md`
- Branch/worktree/PR workflow: `ai_docs/workflow.md`
