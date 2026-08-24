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

## Why the repository runner is not eligible

GitHub warns that fork pull requests can modify workflow YAML and execute dangerous code on a public repository's self-hosted runner. An `if` predicate or hosted router inside that same mutable workflow is not an enforceable trust boundary. Approval settings reduce automatic execution but do not make an approved fork safe.

The registered `darylmcd-windows-dev` runner is repository-level and runs as `LocalSystem`. It must not accept jobs from this public repository. Keep its Windows service stopped and set to manual or disabled until the runner is deregistered or moved behind an infrastructure-enforced boundary. See GitHub's [secure-use reference](https://docs.github.com/en/actions/reference/security/secure-use) and [runner-group access controls](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/manage-access).

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

## Containment and removal

From an elevated PowerShell session, contain the existing service before changing remote registration:

```powershell
Stop-Service -Name 'actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev'
Set-Service -Name 'actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev' -StartupType Manual
```

Verify both local and remote state:

```powershell
Get-CimInstance Win32_Service -Filter "Name='actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev'" |
  Select-Object Name, State, StartMode, StartName

gh api repos/darylmcd/Roslyn-Backed-MCP/actions/runners `
  --jq '.runners[] | {name, status, busy, labels: [.labels[].name]}'
```

Contain the service immediately; this does not depend on merging the hosted-only workflow. To remove the runner permanently:

1. Confirm the service is stopped and manual/disabled, and record its exact installation path.
2. Generate a removal token through the repository runner API.
3. Run `config.cmd remove --token <token>` from `C:\Users\daryl\actions-runner\`.
4. Confirm the runner no longer appears in the repository API.
5. Delete the exact runner installation directory only after deciding whether diagnostics must be retained.

Removal and directory deletion are destructive. Resolve exact targets and preserve any needed diagnostics first.

## Troubleshooting

### A required check is pending after jobs finish

The default-branch ruleset must require exactly `validate`, emitted by `validate-gate` only for pull requests. Confirm `route` and every `validate-leg (...)` job completed. Do not require dynamic matrix leg names or `validate-informational`.

### A local runner appears in a pull-request run

Cancel the run immediately and treat it as a security incident. The checked-in workflow must contain no self-hosted pull-request route. Then confirm the repository runner is stopped/offline and review workflow changes in the pull request before any rerun.
