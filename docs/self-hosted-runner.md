# Self-hosted GitHub Actions runner

This repo supports a hybrid CI model: maintainer PRs run on a self-hosted Windows runner; fork PRs continue to run on GitHub-hosted Linux runners. The hybrid model is set up but **not yet active in CI** — see [Activation](#activation) below.

## Why hybrid

GitHub-hosted runners are 2-vcpu Linux machines that take ~10 min to run the full validate suite. A modern Windows desktop can run the same suite in 2-3 min. The hybrid model gets the wall-clock speedup without the security risk of letting fork PRs execute on the maintainer's machine.

## Security model — read this first

GitHub explicitly warns against using self-hosted runners with public repos because **anyone who opens a PR can run arbitrary code on the runner machine** via malicious workflow YAML or build scripts. The mitigation in this repo:

```yaml
runs-on: ${{ github.event.pull_request.head.repo.fork && 'ubuntu-latest' || ['self-hosted', 'roslynmcp-dev'] }}
```

This conditional says: **fork PRs use GitHub-hosted Linux; non-fork PRs (maintainer's own branches) use the self-hosted runner**. A fork-PR attacker can never reach the self-hosted runner because their `head.repo.fork == true`.

**Residual risk:** if the maintainer ever clones a malicious branch into their own fork+PRs it (e.g., copying someone else's untrusted patch onto their own branch), it would run on the self-hosted runner. Maintainer discipline mitigates this.

## What's already set up

As of 2026-05-08:

- **Runner location:** `C:\Users\daryl\actions-runner\`
- **Runner version:** v2.334.0 (current at install time; auto-updates on next start)
- **Runner name:** `darylmcd-windows-dev`
- **Labels:** `self-hosted`, `Windows`, `X64`, `roslynmcp-dev`, `windows-native`
- **Registration status:** registered with `darylmcd/Roslyn-Backed-MCP` (visible via `gh api repos/darylmcd/Roslyn-Backed-MCP/actions/runners`)
- **Current run-state:** **offline** — agent is configured but not yet running. See [Starting the runner](#starting-the-runner) below.

## Starting the runner

Two options. Install-as-service is recommended for daily use; foreground mode is fine for one-off testing.

### Option A — Install as Windows service (recommended)

Requires elevated PowerShell. The service auto-starts on boot.

```powershell
# Open PowerShell as Administrator, then:
Set-Location C:\Users\daryl\actions-runner
.\svc.cmd install            # install as service running under NetworkService
.\svc.cmd start              # start it
.\svc.cmd status             # verify
```

The service is named `actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev` (long but auto-generated).

To stop / uninstall later:

```powershell
.\svc.cmd stop
.\svc.cmd uninstall
```

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

The CI workflow (`.github/workflows/ci.yml`) currently uses `runs-on: ubuntu-latest` for all runs. To activate the hybrid model, change the validate job's `runs-on:` line to the conditional shape:

```yaml
jobs:
  validate:
    runs-on: ${{ github.event.pull_request.head.repo.fork && 'ubuntu-latest' || fromJSON('["self-hosted", "roslynmcp-dev"]') }}
```

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
2. Stop and uninstall the service per [Option A above](#option-a--install-as-windows-service-recommended).
3. Generate a removal token: `gh api -X POST repos/darylmcd/Roslyn-Backed-MCP/actions/runners/remove-token`.
4. From the runner directory: `.\config.cmd remove --token <removal-token>`.
5. Delete the runner directory: `Remove-Item -Recurse -Force C:\Users\daryl\actions-runner`.

## Cost / benefit

| | Hosted (today) | Self-hosted |
|---|---|---|
| Wall-clock per run | ~10 min | ~2-3 min (estimate; verify on first run) |
| GitHub Actions minutes used | ~10/run | ~0/run (only the docs-detect step uses GitHub minutes) |
| Setup effort | none | one-time service install |
| Maintenance burden | none | runner agent auto-updates; .NET SDK updates manual |
| Security model | GitHub-managed | maintainer-managed (fork-gated for safety) |

For a solo-maintainer repo with frequent PRs, the wall-clock savings compound quickly. The runner agent auto-updates so day-to-day maintenance is minimal — the main ongoing cost is keeping the .NET SDK current as the project's `<TargetFramework>` advances.
