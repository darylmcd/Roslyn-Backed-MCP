# Self-hosted GitHub Actions runner

This repo runs a hybrid CI model (**active since 2026-05-08**): maintainer PRs run on a self-hosted Windows runner; fork PRs, dependabot PRs, workflow_dispatch, and scheduled runs use GitHub-hosted Linux runners. A `route` job (**active since 2026-08-10** — see [Hosted fallback for an offline runner](#hosted-fallback-for-an-offline-runner)) probes the self-hosted runner's live status and falls back maintainer PRs to hosted Linux when it is offline, so a sleeping/wedged box no longer queues PRs indefinitely. See [Activation](#activation) for the exact conditional.

## Why hybrid

GitHub-hosted runners are 2-vcpu Linux machines that take ~10 min to run the full validate suite. A modern Windows desktop can run the same suite in 2-3 min. The hybrid model gets the wall-clock speedup without the security risk of letting fork PRs execute on the maintainer's machine.

## Security model — read this first

GitHub explicitly warns against using self-hosted runners with public repos because **anyone who opens a PR can run arbitrary code on the runner machine** via malicious workflow YAML or build scripts. The mitigation in this repo (the authoritative expression lives in `.github/workflows/ci.yml`):

```yaml
runs-on: ${{ github.event_name == 'pull_request' && !github.event.pull_request.head.repo.fork && github.event.pull_request.user.login != 'dependabot[bot]' && fromJSON('["self-hosted", "roslynmcp-dev"]') || 'ubuntu-latest' }}
```

This conditional says: **only the maintainer's own non-fork, non-dependabot PRs use the self-hosted runner**. A fork-PR attacker can never reach it because their `head.repo.fork == true`. Dependabot PRs are also routed to hosted Linux: although they are non-fork branches, their diffs pull third-party package versions whose code executes during `dotnet test` — a supply-chain compromise of a bumped package must not run on the maintainer's box.

**Residual risk:** if the maintainer ever clones a malicious branch into their own fork+PRs it (e.g., copying someone else's untrusted patch onto their own branch), it would run on the self-hosted runner. Maintainer discipline mitigates this.

## What's already set up

As of 2026-05-08:

- **Runner location:** `C:\Users\daryl\actions-runner\`
- **Runner version:** v2.334.0 (current at install time; auto-updates on next start)
- **Runner name:** `darylmcd-windows-dev`
- **Labels:** `self-hosted`, `Windows`, `X64`, `roslynmcp-dev`, `windows-native`
- **Registration status:** registered with `darylmcd/Roslyn-Backed-MCP` (visible via `gh api repos/darylmcd/Roslyn-Backed-MCP/actions/runners`)
- **Current run-state:** installed as a Windows service under `LocalSystem`; check live status with the [verify command](#verifying-its-running).

## Availability — the single-runner queue

There is exactly one runner, and it lives on the maintainer's interactive desktop. **When it is offline or busy, every maintainer PR queues indefinitely** — observed queue times of 2–14.5 hours happened because the box slept overnight and a wedged job held the slot. Keep it available:

- **Power:** disable system sleep while on AC (`powercfg /change standby-timeout-ac 0`); display sleep is fine. A sleeping box looks *online* to GitHub for a few minutes, then jobs queue silently.
- **Service recovery:** set the runner service to restart on failure (`sc.exe failure "actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev" reset= 86400 actions= restart/60000/restart/60000/restart/60000`).
- **Wedged job:** cancel the run in the Actions UI (`gh run cancel <id>`); the runner frees the slot at job cleanup and picks up the next queued run.
- **Hosted fallback:** a `route` job probes runner status before `validate` starts and falls maintainer PRs back to `ubuntu-latest` when the self-hosted runner is offline — see [Hosted fallback for an offline runner](#hosted-fallback-for-an-offline-runner). This requires an operator-provisioned PAT secret; without it, CI keeps working exactly as before (self-hosted routing, no fallback) rather than blocking.

## Hosted fallback for an offline runner

**Active since 2026-08-10.** `runs-on` is evaluated before any job runs, so
routing around an offline/wedged self-hosted runner needs a job that runs
*first* and feeds its decision through job outputs. `.github/workflows/ci.yml`
has a `route` job (always runs, cheap, `ubuntu-latest`) ahead of `validate`:

- For fork PRs, dependabot PRs, `workflow_dispatch`, and scheduled runs, `route`
  outputs `ubuntu-latest` unconditionally and never calls the runners API —
  the security boundary (§ Security model) is unchanged, because those events
  never reach the self-hosted-eligible code path in the first place.
- For maintainer pull_request events, `route` calls
  `GET /repos/{owner}/{repo}/actions/runners` using a PAT from the
  `RUNNER_STATUS_PAT` repo secret (the workflow `GITHUB_TOKEN` cannot read
  this endpoint — it requires repo-admin scope) and checks for at least one
  runner with `status: online` carrying the `roslynmcp-dev` label.
  - **Runner online:** routes to `["self-hosted", "roslynmcp-dev"]` — same
    behavior as before this row shipped.
  - **Runner offline (or absent from the runners list entirely):** routes to
    `ubuntu-latest` so the PR runs immediately instead of queueing behind the
    single self-hosted slot.
  - **`RUNNER_STATUS_PAT` secret absent, invalid, or the API call fails for
    any other reason:** routes to `["self-hosted", "roslynmcp-dev"]` — the
    known-working current behavior. A missing or broken PAT never blocks or
    breaks CI; it just means the fallback isn't active.

### Operator setup (required for the fallback to actually route)

Without this secret, CI works exactly as it did before this row — self-hosted
routing for maintainer PRs, hosted for fork/dependabot — it just can't detect
an offline runner. To enable the fallback:

1. GitHub → Settings → Developer settings → **Fine-grained personal access
   tokens** → generate new token.
2. Repository access: `darylmcd/Roslyn-Backed-MCP` only.
3. Permissions: **Administration: Read-only** (this is the scope that grants
   read access to `GET /repos/{owner}/{repo}/actions/runners`; no other
   permission is required).
4. Repo → Settings → Secrets and variables → Actions → **New repository
   secret** → name it `RUNNER_STATUS_PAT`, paste the token value.

No workflow change is needed after adding the secret — `route` picks it up
on the next PR run.

## Starting the runner

Two options. Install-as-service is recommended for daily use; foreground mode is fine for one-off testing.

### Option A — Install as Windows service (recommended)

Runner v2.334+ no longer ships `svc.cmd`. Service install is performed by re-running `config.cmd` with the `--runasservice` flag from elevated PowerShell. The service auto-starts on boot.

If you registered the runner WITHOUT `--runasservice` (e.g. an interactive setup), you must deregister and reconfigure:

```powershell
# In a non-elevated shell:
$removeToken = (gh api -X POST repos/darylmcd/Roslyn-Backed-MCP/actions/runners/remove-token --jq '.token')
Set-Location C:\Users\daryl\actions-runner
.\config.cmd remove --token $removeToken

# Then in an Administrator PowerShell:
$regToken = (gh api -X POST repos/darylmcd/Roslyn-Backed-MCP/actions/runners/registration-token --jq '.token')
Set-Location C:\Users\daryl\actions-runner
.\config.cmd --unattended `
  --url https://github.com/darylmcd/Roslyn-Backed-MCP `
  --token $regToken `
  --name 'darylmcd-windows-dev' `
  --labels 'roslynmcp-dev,windows-native' `
  --work '_work' `
  --replace `
  --runasservice
```

`--runasservice` registers the runner AND installs `RunnerService.exe` as a Windows service in one shot. The service name is auto-generated as `actions.runner.<owner>-<repo>.<runner-name>` — e.g. `actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev`.

After config completes, the service is installed but may not be running yet. Start it:

```powershell
$svc = Get-Service | Where-Object { $_.Name -like 'actions.runner.darylmcd-Roslyn-Backed-MCP*' } | Select-Object -First 1
Start-Service -Name $svc.Name
```

**If the service refuses to start under the default `NETWORK SERVICE` account** (timeout, "cannot start in a timely fashion"), switch it to `LocalSystem` instead. The runner directory under a user profile path can have ACL quirks that block `NETWORK SERVICE` despite the auto-granted permissions:

1. Open `services.msc` (Win+R → `services.msc`).
2. Find **GitHub Actions Runner (darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev)**.
3. Right-click → Properties → Log On tab.
4. Switch from "This account" (`NT AUTHORITY\NETWORK SERVICE`) to **"Local System account"**.
5. Apply, then start the service.

`LocalSystem` has machine-wide privileges so it bypasses the user-profile-path ACL issue. The maintainer-only repo + fork-gated runs-on conditional means LocalSystem isn't a meaningful escalation here (LocalSystem already runs anything the maintainer asks of it; the security boundary is at the workflow YAML, not at the service account).

To stop / uninstall later, manage the service via standard Windows tooling:

```powershell
Stop-Service -Name $svc.Name
Remove-Service -Name $svc.Name        # PS 6+
# or:    sc.exe delete $svc.Name
```

Then run `.\config.cmd remove --token <removal-token>` to deregister from GitHub.

### Option B — Foreground mode (one-off testing)

```powershell
Set-Location C:\Users\daryl\actions-runner
.\run.cmd
```

This blocks the terminal until you Ctrl+C. Useful to verify the runner picks up a job correctly before installing as a service.

## Verifying it's running

```bash
gh api repos/darylmcd/Roslyn-Backed-MCP/actions/runners --jq '.runners[] | {name, status, busy}'
```

Healthy state:

```json
{"name":"darylmcd-windows-dev","status":"online","busy":false}
```

When a job is running on it: `status: "online"`, `busy: true`.

## Activation

**Active as of 2026-05-08.** The CI workflow (`.github/workflows/ci.yml`) uses this conditional:

```yaml
jobs:
  validate:
    runs-on: ${{ github.event_name == 'pull_request' && !github.event.pull_request.head.repo.fork && github.event.pull_request.user.login != 'dependabot[bot]' && fromJSON('["self-hosted", "roslynmcp-dev"]') || 'ubuntu-latest' }}
```

The conditional says: ONLY maintainer non-fork, non-dependabot pull_request events route to self-hosted. Everything else (workflow_dispatch, schedule, fork PRs, dependabot PRs) stays on `ubuntu-latest`. This preserves coverage-collection consistency on dispatch/schedule runs (which the workflow only does on hosted Linux), the security boundary on fork/dependabot PRs, and keeps dependabot's PR waves from queueing behind the single self-hosted slot.

**Pre-activation checklist:**

1. Runner shows `status: "online"` per the verify command above.
2. .NET SDK 10.x is installed on the runner machine (matches `global.json`'s `<TargetFramework>`). Verify: `dotnet --list-sdks` shows a `10.0.x` entry.
3. `pwsh` 7+ is on PATH (the workflow uses `shell: pwsh`).
4. `git` is on PATH and configured.
5. The runner has internet access for NuGet restore.

After activation, push a no-op test PR to verify the workflow lands on the self-hosted runner and completes faster than baseline.

**Rollback if anything breaks:** revert the `runs-on:` line to `ubuntu-latest`. Self-hosted runner stays registered but idle.

## Troubleshooting

**Runner shows offline despite `svc start` succeeding.** Check the service: `Get-Service actions.runner.*`. If running but offline in GitHub, check `_diag/Runner_*.log` in the runner directory — common culprit is firewall blocking outbound HTTPS to GitHub.

**Runner picks up the job but fails on `dotnet build`.** Verify .NET SDK 10.x is installed for the service account (NetworkService by default — `dotnet` may not be on its PATH even if it's on yours). Either install machine-wide via the standard installer (default) or run the service under your user account: `.\svc.cmd install $env:USERNAME`.

**Workflow stuck pending after activation.** GitHub queues self-hosted jobs that don't match available runners. Re-verify the labels in the runs-on conditional match the runner's labels (`roslynmcp-dev`).

## Removing the runner

If you decide to abandon the self-hosted approach:

1. Revert `.github/workflows/ci.yml` to `runs-on: ubuntu-latest` if activated.
2. Stop the service: `Stop-Service -Name <service-name>` (find via `Get-Service | Where-Object { $_.Name -like 'actions.runner.*' }`).
3. Generate a removal token: `gh api -X POST repos/darylmcd/Roslyn-Backed-MCP/actions/runners/remove-token`.
4. From the runner directory: `.\config.cmd remove --token <removal-token>` — this deregisters from GitHub AND uninstalls the Windows service.
5. Delete the runner directory: `Remove-Item -Recurse -Force C:\Users\daryl\actions-runner`.

## Cost / benefit

| | Hosted (today) | Self-hosted |
|---|---|---|
| Wall-clock per run | ~10-12 min | ~9-11 min observed at ~1700 tests (the early ~2-3 min estimate predates suite growth); PR runs drop further with `-ExcludeNetworkTests` |
| GitHub Actions minutes used | ~10/run | ~0/run (only the docs-detect step uses GitHub minutes) |
| Setup effort | none | one-time service install |
| Maintenance burden | none | runner agent auto-updates; .NET SDK updates manual |
| Security model | GitHub-managed | maintainer-managed (fork-gated for safety) |

For a solo-maintainer repo with frequent PRs, the wall-clock savings compound quickly. The runner agent auto-updates so day-to-day maintenance is minimal — the main ongoing cost is keeping the .NET SDK current as the project's `<TargetFramework>` advances.
