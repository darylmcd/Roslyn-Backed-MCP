# Self-hosted GitHub Actions runner

This repo runs a hybrid CI model (**active since 2026-05-08**): maintainer PRs run on a self-hosted Windows runner; fork PRs, dependabot PRs, workflow_dispatch, and scheduled runs use GitHub-hosted Linux runners. A `route` job (**active since 2026-08-10** — see [Hosted fallback for an offline runner](#hosted-fallback-for-an-offline-runner)) probes the self-hosted runner's live status and falls back maintainer PRs to hosted Linux when it is offline, so a sleeping/wedged box no longer queues PRs indefinitely. Since 2026-08-19, self-hosted routing also fans `validate` out to a second, non-primary `ubuntu-latest` matrix leg so every maintainer PR is validated on the same OS the publish gate uses (merge/publish OS parity — see CI_POLICY.md § Merge Gating Expectations). See [Activation](#activation) for the exact conditional.

## Why hybrid

GitHub-hosted runners are 2-vcpu Linux machines that take ~10 min to run the full validate suite. A modern Windows desktop can run the same suite in 2-3 min. The hybrid model gets the wall-clock speedup without the security risk of letting fork PRs execute on the maintainer's machine.

## Security model — read this first

GitHub explicitly warns against using self-hosted runners with public repos because **anyone who opens a PR can run arbitrary code on the runner machine** via malicious workflow YAML or build scripts. The mitigation in this repo (**active since 2026-08-10** — the authoritative predicate now lives in the `route` job's maintainer-PR check, `.github/workflows/ci.yml:86`; see [Hosted fallback for an offline runner](#hosted-fallback-for-an-offline-runner)):

```yaml
$isMaintainerPr = "${{ github.event_name == 'pull_request' && !github.event.pull_request.head.repo.fork && github.event.pull_request.user.login != 'dependabot[bot]' }}"
```

This says: **only the maintainer's own non-fork, non-dependabot PRs are even eligible for the self-hosted runner**. A fork-PR attacker can never reach it — `route` detects `head.repo.fork == true` and routes to `ubuntu-latest` unconditionally, without ever calling the runners API. Dependabot PRs are routed to hosted Linux the same way: although they are non-fork branches, their diffs pull third-party package versions whose code executes during `dotnet test` — a supply-chain compromise of a bumped package must not run on the maintainer's box.

`validate`'s own `runs-on` (`.github/workflows/ci.yml:174`) is just `${{ matrix.leg.runs_on }}`, fed by a matrix over `route`'s `runner_matrix` output — it has no security logic of its own; it consumes whatever `route` decided. The security boundary lives entirely in `route`'s maintainer-PR check above, which runs before any runners-API call and is unaffected by that call's outcome. The runner matrix cannot widen that boundary: `route` only ever emits a self-hosted matrix entry on the maintainer-PR branch that passes the check above, and the extra parity leg it adds there targets `ubuntu-latest`, never the self-hosted box — fork and dependabot PRs still get a single hosted leg with no API call.

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

**Active as of 2026-05-08 (routing mechanism updated 2026-08-10, runner matrix + OS-parity leg added 2026-08-19 — see [Hosted fallback for an offline runner](#hosted-fallback-for-an-offline-runner)).** The CI workflow (`.github/workflows/ci.yml`) splits routing across jobs instead of one inline `runs-on:` conditional: a `route` job (`ci.yml:40-142`) computes the target runner **matrix**, `validate` (`ci.yml:144-329`) fans out over it reporting per-leg checks named `validate-leg (<os>)`, and the `validate-gate` aggregator (`ci.yml:340-354`) carries the `name: validate` that the default-branch ruleset already requires — so the fan-out needs no branch-protection change:

```yaml
jobs:
  route:
    outputs:
      runner_matrix: ${{ steps.decide.outputs.runner_matrix }} # list of validate legs (only output)
    steps:
      - id: decide
        run: |
          # $selfHostedLabels is the single source for the runner labels:
          # both matrix payloads and the online-runner filter derive from it.
          $isMaintainerPr = "${{ github.event_name == 'pull_request' && !github.event.pull_request.head.repo.fork && github.event.pull_request.user.login != 'dependabot[bot]' }}"
          # non-maintainer events -> single ubuntu-latest leg (no API call, ever)
          # maintainer PRs -> probes the runners API; self-hosted routing emits
          # TWO legs: {self-hosted windows, primary} + {ubuntu-latest linux,
          # non-primary} for merge/publish OS parity. Hosted routing (runner
          # offline) emits a single ubuntu-latest primary leg. Missing/broken
          # PAT falls back to self-hosted routing (never blocks CI).

  validate:
    name: validate-leg (${{ matrix.leg.os }})   # per-leg check names; never pin these
    needs: route
    strategy:
      fail-fast: false
      matrix:
        leg: ${{ fromJSON(needs.route.outputs.runner_matrix) }}
    runs-on: ${{ matrix.leg.runs_on }}
    # artifact uploads run only where matrix.leg.primary == true

  validate-gate:
    name: validate            # the context the ruleset already requires
    needs: validate
    if: always()
    runs-on: ubuntu-latest
    # fails unless every validate leg succeeded — THIS reports `validate`
```

The routing decision says the same thing it always has: ONLY maintainer non-fork, non-dependabot pull_request events are even eligible for self-hosted — and, since 2026-08-10, only when the runner is actually online (see the fallback section above). Everything else (workflow_dispatch, schedule, fork PRs, dependabot PRs) stays on `ubuntu-latest`, decided by `route` without ever calling the runners API. This preserves coverage-collection consistency on dispatch/schedule runs (which the workflow only does on hosted Linux), the security boundary on fork/dependabot PRs, and keeps dependabot's PR waves from queueing behind the single self-hosted slot. The 2026-08-19 parity leg means a self-hosted-routed PR additionally runs the full `eng/verify-release.ps1` suite on `ubuntu-latest` concurrently (not queued behind the self-hosted slot), closing the v3.0.1 gap where an OS-sensitive test passed pre-merge on Windows and then failed the Linux publish gate at tag time.

**Pre-activation checklist:**

1. Runner shows `status: "online"` per the verify command above.
2. .NET SDK 10.x is installed on the runner machine (matches `global.json`'s `<TargetFramework>`). Verify: `dotnet --list-sdks` shows a `10.0.x` entry.
3. `pwsh` 7+ is on PATH (the workflow uses `shell: pwsh`).
4. `git` is on PATH and configured.
5. The runner has internet access for NuGet restore.

After activation, push a no-op test PR to verify the workflow lands on the self-hosted runner and completes faster than baseline.

**Rollback if anything breaks:** remove `validate`'s `strategy:` block, set its `runs-on:` (`ci.yml:174`) directly to `ubuntu-latest`, delete the two `matrix.leg.primary == true` clauses from the upload/coverage step conditions, and drop its `name:` override (and drop `needs: route`, or leave it — an unused `needs` is harmless). Keep the `validate-gate` job: it is what reports the required `validate` context, and with a single validate job it simply mirrors its result. Removing it requires renaming the surviving job to `validate` in the same edit, or the ruleset waits forever on a context nothing reports. The `route` job can stay in place unused, or be deleted along with it. Self-hosted runner stays registered but idle.

## Troubleshooting

**Runner shows offline despite `svc start` succeeding.** Check the service: `Get-Service actions.runner.*`. If running but offline in GitHub, check `_diag/Runner_*.log` in the runner directory — common culprit is firewall blocking outbound HTTPS to GitHub.

**Runner picks up the job but fails on `dotnet build`.** Verify .NET SDK 10.x is installed for the service account (NetworkService by default — `dotnet` may not be on its PATH even if it's on yours). Either install machine-wide via the standard installer (default) or run the service under your user account: `.\svc.cmd install $env:USERNAME`.

**Workflow stuck pending after activation.** GitHub queues self-hosted jobs that don't match available runners. Re-verify the label list the `route` job emits (`$selfHostedLabels = @('self-hosted', 'roslynmcp-dev')` in `ci.yml` — the single source for both matrix payloads and the online-runner filter, consumed by `validate`'s `runs-on: ${{ matrix.leg.runs_on }}`) matches the runner's registered labels (`roslynmcp-dev`).

## Removing the runner

If you decide to abandon the self-hosted approach:

1. Revert `.github/workflows/ci.yml`'s `validate` job to a plain `runs-on: ubuntu-latest` (drop the `strategy:` matrix, the `matrix.leg.*` references, and the `route` job entirely, since nothing else depends on them; keep `validate-gate` so the required `validate` context is still reported, or rename the surviving job to `validate` in the same edit).
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
