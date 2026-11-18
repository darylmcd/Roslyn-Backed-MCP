# CI Policy

This document is the canonical validation and merge-gating contract: current rules only. History and measurements: `ai_docs/references/ci-topology-decision-log.md`. Workflow, planner, profile and cache maintenance: `ai_docs/references/ci-internals.md`.

## Trigger And Routing Contract

| Event / trust state | Validation topology | Coverage / network |
|---|---|---|
| Evidence-only pull request (`ai_docs/audits/**`, `ai_docs/reports/**`, `ai_docs/items/**`, `audit-reports/**`, minus the exclusions below) | One pwsh `evidence-lint` job (changelog contract + `verify-ai-docs.ps1`); no .NET setup, build, or test legs; SDK-floor probe and quality job skipped | Not applicable |
| Policy-only documentation pull request (Markdown / `ai_docs/**/*.json` / the tracked promotion scorecard, excluding behavior-bearing paths and Markdown inside a source root) | One hosted Linux leg, the artifact owner: every gate of the release verifier (every release policy gate plus publish/hash) and the docs-tier consumer test classes only (§ Docs Route Test Selection); SDK-floor probe and quality job skipped | Coverage and `Network` tests skipped; audit skipped |
| Any code-bearing pull request | Six hosted Windows shards + five hosted Linux shards + one concurrent exact-SDK-floor build/workspace probe + one concurrent quality job | Coverage and `Network` tests skipped |
| Merge-queue group (`merge_group`) | Always the full code-bearing topology (no trusted changed-file set, so no docs-only/evidence-only route) | Coverage and `Network` tests skipped |
| Manual dispatch / weekly schedule (Mon 05:45 UTC) | One unsharded hosted Linux suite + one concurrent exact-SDK-floor build/workspace probe + one concurrent quality job | Coverage and live `Network` tests enabled |

Jobs of `ci.yml`:

| Job | Runs on | Cap (min) | Runs when |
|---|---|---:|---|
| `route` | `ubuntu-latest` | 3 | Always |
| `validate-leg (<name>)` matrix | Per leg (`runs_on`) | Per leg (`timeout_minutes`): Windows 45; Linux code 45 (TEMPORARY, restore 30 via row `ci-restore-linux-leg-timeout-30-after-rebalance`); docs leg 30; dispatch/schedule leg 90 | Not on the evidence route |
| `sdk-floor (11.0.100)` | `ubuntu-latest` | 15 | Dispatch, schedule, merge group, code PRs (not docs or evidence PRs) |
| `quality` | `ubuntu-latest` | 30 | Dispatch, schedule, merge group, code PRs |
| `evidence-lint` | `ubuntu-latest` | 5 | Evidence route only |
| `validate-gate` | `ubuntu-latest` | 5 | Always (`if: always()`) |

Rules:

- Do not add a push-to-`main` trigger. Protected changes arrive through an already-validated pull request.
- Cancel superseded pull-request runs. Never cancel dispatch or scheduled runs.
- Route all public-repository pull-request code only to GitHub-hosted runners. A repository-level self-hosted runner is not protected by an in-workflow fork predicate because a fork can modify the workflow itself.
- Keep the repository-level self-hosted runner inventory empty while this repository remains public under a personal account. A future hybrid lane requires the infrastructure-enforced authorization and disposable-worker boundaries in `docs/self-hosted-runner.md` before registration.
- Classify both `filename` and `previous_filename` from the pull-request files API. A source-to-doc rename is code-bearing because its removed source path still requires full validation.
- Require the files API record count to equal `pull_request.changed_files` and remain below the endpoint's 3,000-file cap before selecting the docs-only route. A capped or incomplete enumeration is code-bearing.
- Treat behavior-bearing Markdown under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, and `.github/prompts/` as code-bearing.
- Treat Markdown under `src/`, `analyzers/`, `samples/`, and `tests/` as code-bearing: it is a build, pack or fixture input (analyzer release-tracking files `AnalyzerReleases.*.md`, the packed `src/RoslynMcp.Host.Stdio/README.md`, which is also the registry ownership marker, and sample or test fixtures).
- Docs-tier files feed release gates: `CHANGELOG.md` (version drift, breaking-version bump), `THIRD-PARTY-NOTICES.md` (restored-license verification), allowlisted plugin files (package allowlist). The docs-route leg is therefore the artifact owner and runs every gate of `verify-release.ps1`, never `-TestShardOnly`. `-DocsTierConsumerTests` narrows only the test set.
- A pull request takes the highest tier any of its paths (including rename origins) needs: code > docs > evidence.
- The evidence tier is limited to paths no test or `eng/` script reads. Files inside evidence roots that CI does read stay docs tier through `$evidenceExclusions` in `eng/resolve-ci-topology.ps1` (currently `ai_docs/items/backlog-d-fragment-schema.md` and `audit-reports/_latest-promotion-scorecard.json`). `CiEvidenceTierConsumerContractTests` fails on any new reference to an evidence root until it is classified.
- Never use workflow-level `paths`/`paths-ignore` to skip validation: the required `validate` check would never report. Skips happen inside the workflow with `validate-gate` still reporting.
- On the evidence route `validate-gate` passes only when `evidence-lint` succeeded and the build/test matrix, SDK-floor probe, and quality job were skipped, never failed or cancelled.
- Keep the pull-request `validate-gate` check named `validate`. Merge-queue groups (`merge_group`) also report `validate`. Dispatch/schedule runs emit `validate-informational` so an informational one-leg run cannot satisfy the pull-request ruleset context.
- The default-branch ruleset (`Default Branch Ruleset`, id 14515474) has no merge-queue rule (verified 2026-11-17 via `gh api repos/darylmcd/Roslyn-Backed-MCP/rulesets/14515474`), so the `merge_group` trigger is dormant until one is added. The changed-file format gate diffs against `github.event.merge_group.base_sha`.

The routing decision (leg construction, docs-only classification, fail-closed count-mismatch/cap guard) is a pure function in `eng/resolve-ci-topology.ps1`, table-tested by `CiTopologyDecisionContractTests`. The `route` job only performs the `gh api` file listing and passes it in; it never reimplements the decision inline.

The router emits typed leg fields. Keep their concerns separate:

| Field | Meaning |
|---|---|
| `runs_on` | GitHub-hosted runner label |
| `artifact_owner` | Sole policy/release-artifact owner |
| `timeout_minutes` | Explicit per-leg job cap |
| `test_shard_index`, `test_shard_count` | Complete class partition passed to `verify-release.ps1` |
| `test_scope` | `full` = this leg's shard of the whole suite; `docs-consumers` = only the classes in `eng/docs-tier-test-consumers.json`. The workflow fails a leg with any other value |

## Pull-Request Test Partition

- Code pull requests and merge-queue groups run six concurrent Windows class shards and five concurrent Linux class shards, each on its own GitHub-hosted runner (§ Linux Shard Count Decision).
- Policy-only documentation PRs run one Linux leg of docs-tier consumer classes, not a partition of the suite (§ Docs Route Test Selection). Do not restore a zero-test docs route: repository tests consume Markdown contracts, and `DocsTierTestConsumerContractTests` keeps a new consumer covered. The evidence route is the one zero-test route, safe only for paths `CiEvidenceTierConsumerContractTests` proves no test reads.
- `eng/get-test-shard-plan.ps1` reads compiled MSTest metadata and must fail on an invalid index/count, missing assembly, unreadable metadata, unsafe class name, zero discovered classes, empty shard, overlap, or incomplete assignment.
- Shards use exact MSTest `ClassName` predicates. Do not use `FullyQualifiedName~` prefix matching.
- `eng/ci.runsettings` enables `TreatNoTestsAsError`; an accidentally empty filter is a failure.
- Keep pull-request coverage disabled. Keep dispatch/schedule coverage unsharded until coverage merge semantics are explicitly implemented.
- Keep matrix `fail-fast: false` so independent OS/shard failures stay visible in one run.
- Keep the exact SDK-floor lane isolated from hosted-image SDKs and bounded to restore, Release build, a representative MSBuildWorkspace load, and the native fork admission late-child metadata/copy cases. The latest-`10.0.x` matrix owns the complete suite.

## Docs Route Test Selection

Operator decision 2026-11-17; evidence in the decision log. A policy-only documentation pull request runs every non-test gate and only the docs-tier consumer test classes. Code pull requests, merge-queue groups and dispatch/schedule runs run the whole suite.

| Part | Contract |
|---|---|
| Topology | One leg, `docs-linux-1-of-1`: `ubuntu-latest`, artifact owner, 30-minute cap, `test_shard_count` 1, `test_scope` `docs-consumers` |
| Non-test gates | `verify-ai-docs.ps1`, `verify-actionlint.ps1`, and every gate of `verify-release.ps1` (package family, version drift, shipped skills, package allowlist, changelog fragments, breaking version, registry readiness, restored licenses, Release build, publish/hash) |
| Tests | Exactly the classes under `consumers` in `eng/docs-tier-test-consumers.json`, as an exact `ClassName=` filter with the same category filter as every pull-request leg (`eng/verify-release.ps1 -DocsTierConsumerTests`) |
| Calibration | Skipped: the Linux build-calibration steps require `test_shard_count > 1` |
| Required check | `validate-gate` still requires the validate matrix (this leg) to succeed |

Fail-closed rules of the verifier (each fails the leg; none can pass with fewer tests):

- List file missing, empty, not JSON, without `consumers`, or with zero entries.
- An entry without a plain class name (`[A-Za-z_][A-Za-z0-9_.]*`), or a class listed twice.
- A listed class that `eng/get-test-shard-plan.ps1` does not find in the built test assembly.
- A listed class with no executed result in the TRX.
- `-DocsTierConsumerTests` combined with `-TestShardOnly` or a shard count above 1.
- A `test_scope` value the workflow does not know.

Guard contract (`DocsTierTestConsumerContractTests`; its class summary lists the signals and blind spots):

- Every test class, helper or fixture script with a detectable docs-tier signal (path token, docs-reading script, repository tree walk, inheritance from a signal-carrying type) must be a `consumers` entry or a `nonConsumers` entry with a reason; a signal-carrying helper under `tests/` needs a `reviewedHelperFiles` entry.
- The guard checks that a classification was made, not that it is right. A wrong `nonConsumers` reason is a review defect.
- It also fails on a listed class that no longer exists or carries no signal, on a consumer whose every case is `Benchmark` or `Network`, and on an unsorted or malformed list.
- When in doubt, list a consumer: an extra class costs seconds, a missing one is an undetected break.
- Exposure: a docs-tier change that breaks a class missing from the list is not caught on that docs PR. The next code-bearing PR or the Monday schedule catches it; both run the whole suite.

## Required Checks And Artifacts

| Concern | Owner / condition |
|---|---|
| Release policy checks (version, skills, package allowlist, changelog, breaking version, registry readiness, restored licenses) | Artifact-owning leg of code and policy-doc routes, through the full release verifier |
| Changelog-fragment validation | Full release verifier on the artifact-owning leg of code and policy-doc routes; `evidence-lint` on the evidence route |
| AI-doc and shipped-skill validation | Artifact-owning leg; includes policy-doc PRs |
| Restore, Release build, test | Every pull-request/test leg |
| Exact declared SDK floor | Concurrent `sdk-floor` job on code PRs, merge groups, dispatch, and schedule; exact version assertion + restore/build + representative MSBuildWorkspace load + native fork admission late-child metadata/copy cases |
| Publish/hash | Artifact-owning leg of code and policy-doc routes (uploaded from code routes only) |
| Fail-closed NuGet vulnerability audit (`eng/verify-nuget-audit.ps1`: `NuGetAuditMode=all`, NU1900-NU1904 as errors; also `just vuln-audit`) | Concurrent Linux `quality` job on code PRs, merge groups, dispatch, and schedule; independent of test shards and SDK floor |
| Changed-file formatter gate | `quality` job on code PRs and merge groups, after audit restore and a checked Debug build of the repository analyzer; new ENDOFLINE, FINALNEWLINE, IDE1006, IMPORTS, and WHITESPACE findings on touched files fail, tracked baseline debt is reported not suppressed |
| `host-stdio-publish`, `release-manifests` | Artifact-owning non-doc leg; 14 days |
| Per-leg timing summary + TRX (`test-results-<leg>`) | Every pull-request/test leg, uploaded with `always()`; 14 days |
| Cobertura + HTML `code-coverage` | Dispatch/schedule artifact owner; 30 days |

`eng/verify-release.ps1` stays a standalone release gate with the complete default. CI passes `-TestShardOnly` only on non-owner legs so platform-neutral policy and publish/hash work runs once, on the artifact owner. Step detail: `ai_docs/references/ci-internals.md`.

## Hosted Shard Weighting Decision

Standing decision (history: decision log). Hosted shard weighting is the per-image duration profile `eng/shard-duration-profile.json`, not static case counts.

| Rule | Detail |
|---|---|
| Windows partition | Fixed by the profile |
| Linux partition | The profile, additionally scaled per run by the calibration build of each Linux leg (`ai_docs/references/ci-internals.md` § Linux runner capacity) |
| Local and unsharded runs | Static case weighting |
| Profile source | Five complete green first-attempt hosted PR code runs; never a local measurement or a red/cancelled run |
| Hand edits | Never. Regenerate by the refresh checklist (`ci-internals.md` § Duration-profile refresh) after material test-class or hosted-image changes |
| Invalid profile | Missing image, malformed profile, fewer than five source runs, or no matching current classes fails closed; never reuse an invalid profile silently |
| Re-check trigger | The shard count changes, non-test work moves between legs, or a hosted image's achievable gain exceeds its single-leg noise band across at least five complete green runs |
| Fallback weighting | A class absent from the profile or with a changed static weight is weighted by static cases times the image's mean seconds per case, so it can mis-weight a leg until the next refresh (current gap: `ci-windows-six-shard-rebalance-remeasure`) |

### Linux runner capacity

Each sharded Linux leg times the same checked Release build; the planner gives faster runners more work. Every leg waits for all sibling calibration builds under a 3-minute resolver limit, so one late or failed sibling fails every Linux leg until rerun (`ci-linux-calibration-sibling-wait-coupling`). Mechanics: `ai_docs/references/ci-internals.md`.

### Linux Shard Count Decision

Current: code route six Windows (`windows-hosted-N-of-6`) + five Linux (`linux-N-of-5`); the last Linux shard is the sole artifact owner and Windows shard index 0 writes the NuGet cache (operator decision 2026-11-17). Change a count only after re-measuring five complete green runs on the current topology.

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
| Optional local PR-equivalent aggregate (the merge proof is the hosted `validate` check, § Merge Proof) | `just ci` |
| Formatter debt on touched C# files | `pwsh -NoProfile -File ./eng/verify-changed-format.ps1` |
| GitHub Actions workflow YAML/expression lint | `pwsh -NoProfile -File ./eng/verify-actionlint.ps1` |
| Informational coverage/live-network gate | `just full` |
| One release shard | `pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -TestShardIndex <zero-based> -TestShardCount <count>` |
| The policy-docs leg (every release gate, docs-tier consumer classes only) | `pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -DocsTierConsumerTests` |

`just ci` composes `verify-docs`, the changed-file formatter gate, the checksum-pinned actionlint gate, unsharded PR-equivalent release validation (`verify-release-pr`, which owns the shipped-skill check) and the vulnerability audit. The same actionlint script runs on the hosted artifact-owner PR leg. Local validation stays unsharded so one command proves the complete suite.

## Merge Proof

Required check: `validate`, the `validate-gate` job of `.github/workflows/ci.yml` on `pull_request` (ruleset `Default Branch Ruleset`, context `validate`). A green `validate` on the PR head commit is the merge proof: the ship helper accepts it with `--ci-proof=validate`; a local `just ci` run is optional.

Basis: every `just ci` step runs inside `validate`, or cannot change outcome on that route. For the policy-doc route's tests that claim rests on a guard test, not on a proof.

| `just ci` step | Code route | Policy-doc route | Evidence route |
|---|---|---|---|
| `verify-docs` (`eng/verify-ai-docs.ps1`) | artifact owner | artifact owner | `evidence-lint` |
| `verify-changed-format` | `quality` | not run: the gate diffs `*.cs` only; a docs-tier PR changes none | not run: same |
| `verify-actionlint` | artifact owner | artifact owner | not run: `.github/**` is code tier |
| `verify-release-pr` release gates + publish/hash | artifact owner (full verifier) | artifact owner (full verifier) | `evidence-lint` runs the changelog contract; other gates read no evidence path |
| `verify-release-pr` tests (`TestCategory!=Benchmark&TestCategory!=Network`, Release) | complete class partition, Windows x6 + Linux x5 | docs-tier consumer classes only, one Linux leg (`eng/docs-tier-test-consumers.json`); the other classes are not run | not run: no test reads an evidence path (`CiEvidenceTierConsumerContractTests`) |
| `vuln-audit` (`eng/verify-nuget-audit.ps1`) | `quality` | not run: package inputs (`Directory.Packages.props`, `*.csproj`, `global.json`) are code tier | not run: same |
| (extra, not in `just ci`) exact SDK floor | `sdk-floor` | skipped | skipped |

- A skipped step is change-independent, so it reports what it reported on the base. The weekly schedule and every code PR re-run the audit against fresh advisories. Re-classify any path a skipped step starts to read, or the merge-proof claim lapses.
- Policy-doc residual: see the exposure rule in § Docs Route Test Selection.

## Merge Gating Expectations

- Do not declare merge-ready while any required validation leg is failing, cancelled, skipped, or pending.
- `validate-gate` depends on `route`, the complete `validate` matrix, `sdk_floor`, `quality`, and `evidence_lint`; it reports `validate` only when routing and every job required by that route succeeded. Code routes require matrix, SDK floor, and quality success with evidence lint skipped. Policy-doc routes require matrix success with floor, quality, and evidence lint skipped. Evidence routes require evidence lint success with matrix, floor, and quality skipped. Missing or unexpected results fail closed.
- Dispatch/schedule runs report `validate-informational`, never the required pull-request context. They add unsharded Linux coverage, live-network canaries and the SDK-floor probe, and cannot authorize release/package publication, deployment, or SDK generation.
- Do not pin dynamic per-leg names in branch protection.
- Preserve Linux pre-merge coverage: NuGet publication validates on Linux, which closes the Windows-pass/Linux-publish-fail gap.
- Synchronize the branch before merge when repository protection requires it.

## Repository Settings

- CodeQL uses GitHub default setup for C#, GitHub Actions and Python (verified 2026-11-17 via `gh api repos/darylmcd/Roslyn-Backed-MCP/code-scanning/default-setup`). Do not add a duplicate advanced workflow while default setup is active.

## Ownership

| Concern | File | Guard |
|---|---|---|
| Workflow implementation | `.github/workflows/ci.yml` | `CiRunnerParityContractTests`, `ActionlintGateContractTests` |
| Validation-topology decision (pure function) | `eng/resolve-ci-topology.ps1` | `CiTopologyDecisionContractTests`, `CiEvidenceTierConsumerContractTests` |
| Policy-docs test selection | `eng/docs-tier-test-consumers.json` | `DocsTierTestConsumerContractTests` |
| Shard plan and weighting | `eng/get-test-shard-plan.ps1`, `eng/shard-duration-profile.json` | `TestShardPlanContractTests` |
| Maintenance detail | `ai_docs/references/ci-internals.md` | n/a |
| Decision history | `ai_docs/references/ci-topology-decision-log.md` | n/a |
| Human self-hosted operations | `docs/self-hosted-runner.md` | n/a |
| Runtime and local commands | `ai_docs/runtime.md` | n/a |
| Branch/worktree/PR workflow | `ai_docs/workflow.md` | n/a |
