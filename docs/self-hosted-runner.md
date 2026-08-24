# Self-hosted GitHub Actions runner

This repository is public. Pull-request validation therefore runs only on ephemeral GitHub-hosted runners. The canonical topology and merge-gating contract is [`CI_POLICY.md`](../CI_POLICY.md).

## Current topology

| Situation | Windows | Linux |
|---|---|---|
| Code-bearing PR | Four hosted class shards | Two hosted class shards |
| Policy-only documentation PR | No Windows leg | Two hosted class shards |
| Dispatch / weekly schedule | None | One unsharded hosted coverage/live-network leg |

Each OS partition is complete and disjoint. `eng/get-test-shard-plan.ps1` derives exact class filters from the compiled MSTest assembly; the release verifier rejects invalid, empty, overlapping, or incomplete plans.

The stable pull-request check remains `validate`. Dispatch and scheduled runs report `validate-informational`; they cannot substitute their single Linux leg for the event-specific routed pull-request matrix (six legs for code, two for policy-only documentation).

## Retired repository runner

GitHub warns that fork pull requests can modify workflow YAML and execute dangerous code on a public repository's self-hosted runner. An `if` predicate or hosted router inside that same mutable workflow is not an enforceable trust boundary. Approval settings reduce automatic execution but do not make an approved fork safe.

Runner id 22 (`darylmcd-windows-dev`) was deregistered on 2026-08-24. The repository runner API then reported zero registered runners, so public-repository workflow YAML can no longer select the local machine. Keep that inventory empty. See GitHub's [secure-use reference](https://docs.github.com/en/actions/reference/security/secure-use) and [runner-group access controls](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/manage-access).

Remote deregistration stopped the corresponding Windows service, but the non-elevated session could not change its `Auto` startup mode or delete it. The retired installation was moved to `C:\Users\daryl\actions-runner.retired-20260824`, so the service's configured `C:\Users\daryl\actions-runner\bin\RunnerService.exe` path is absent and cannot launch in the observed filesystem state. The orphaned service record and quarantined installation still require elevated/local cleanup; that separate security task is tracked in `ai_docs/backlog.md`.

## Safe hybrid prerequisites

Do not restore a local Windows shard merely by adding `runs-on: [self-hosted, ...]`. Hybrid validation requires all of the following:

1. Move execution off the repository-level runner.
2. Use an isolated, disposable worker with no workstation credentials, persistent developer state, or trusted-network reachability; destroy it after one job.
3. Enforce base-controlled runner authorization. For an organization/enterprise repository, restrict a runner group to selected repositories and a dedicated workflow pinned to a base-branch ref or full SHA. For a personal repository, use a separate private control plane that provisions the disposable worker and reports status for an authorized commit.
4. Ensure fork-authored workflow YAML cannot select the runner.
5. Ensure only base-controlled automation can authorize a trusted same-repository head SHA.
6. Report the local shard against that exact SHA with a distinct required check; never let dispatch/schedule substitute for it.
7. Run the local and hosted Windows partitions concurrently. Keep the fully-hosted four-shard fallback until repeated TRX evidence proves the local path.

A personal-account repository cannot create an organization runner group. Moving the repository to an organization or using a separate private/isolation control plane is therefore an infrastructure prerequisite, not a workflow-only edit.

## Timing evidence

The 2026-08-24 pre-refactor baseline was test-bound:

| Metric | Repository-level Windows | Hosted Linux |
|---|---:|---:|
| Median full job (last five successful PRs) | 16m09s | 25m10s |
| Median tests | 15m23s | 23m40s |

The local machine exposes 24 logical processors and 32 GB RAM. GitHub's standard public-repository `windows-latest` and `ubuntu-latest` runners currently expose 4 CPUs and 16 GB RAM. Standard hosted runners are free and unlimited for public repositories. See [GitHub-hosted runner specifications](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

The first hosted two-shard calibration completed Windows in 19m50s and 16m29s, so it did not beat the repository-level median. Its TRX durations drove the four-shard topology. Uploaded per-leg TRX files remain the source for future balancing; do not treat a static case-count estimate as measured performance.

## Residual local service removal

Remote registration is already absent and the configured executable path no longer exists. From an elevated PowerShell session, remove the stopped orphaned service before deleting the quarantined installation:

```powershell
$ErrorActionPreference = 'Stop'
$serviceName = 'actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev'
Stop-Service -Name $serviceName -Force -ErrorAction Stop
Set-Service -Name $serviceName -StartupType Disabled
sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
    throw "Service deletion failed with exit code $LASTEXITCODE."
}
```

Verify the service is absent and remote registration remains empty:

```powershell
$serviceRecord = Get-CimInstance Win32_Service -Filter "Name='actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev'"
if ($null -ne $serviceRecord) {
    throw "Retired runner service still exists."
}

$runnerInventory = gh api repos/darylmcd/Roslyn-Backed-MCP/actions/runners | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $runnerInventory.total_count -ne 0) {
    throw "Repository runner inventory is not verifiably empty."
}
```

Only after the service query returns no record, prove the original service path remains absent, then resolve and remove the exact quarantined installation:

```powershell
$originalRunnerRoot = 'C:\Users\daryl\actions-runner'
$expectedRunnerRoot = 'C:\Users\daryl\actions-runner.retired-20260824'
if (Test-Path -LiteralPath $originalRunnerRoot) {
    throw "Refusing cleanup because the retired service path exists: $originalRunnerRoot"
}

$runnerRoot = (Resolve-Path -LiteralPath $expectedRunnerRoot).Path
if ($runnerRoot -ne $expectedRunnerRoot) {
    throw "Refusing to remove unexpected runner root: $runnerRoot"
}

Remove-Item -LiteralPath $runnerRoot -Recurse -Force
```

Service and directory deletion are destructive. Preserve any needed diagnostics and re-check both exact targets immediately before removal.

## Troubleshooting

### A required check is pending after jobs finish

The default-branch ruleset must require exactly `validate`, emitted by `validate-gate` only for pull requests. Confirm `route` and every `validate-leg (...)` job completed. Do not require dynamic matrix leg names or `validate-informational`.

### A local runner appears in a pull-request run

Cancel the run immediately and treat it as a security incident. The checked-in workflow must contain no self-hosted pull-request route. Then confirm the repository runner is stopped/offline and review workflow changes in the pull request before any rerun.
