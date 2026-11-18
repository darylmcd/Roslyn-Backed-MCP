# agents-devbuild-runner-pilot-stale-tools — Update the DevBuild-Runner pilot section to the current runner tools

**row:** `agents-devbuild-runner-pilot-stale-tools` · **pri:** `Low` · **size:** `S` · **verified:** `14f4831243de873c2ca917e4edc954dcdbea0edb`

## Anchors

- `AGENTS.md` § DevBuild-Runner pilot — names `server_info`, `job_get`, `job_logs`, a `requestId`, and runner job `roslyn-backed-mcp` / `ci`.

## Acceptance

- [ ] The section names the current tools (`runners`, `submit`, `status`, `logs`) and the current evidence fields (`state`, `exit_code`), or is removed if the pilot is retired.
- [ ] The parity rule still states what counts as evidence and what a mismatch means.

## Evidence

- 2026-11-18: the DevBuild-Runner `README.md` states that the .NET worker is gone and that `job_submit`, `job_get`, `server_info` became `submit`, `status`, `runners`. The coordinator CLI on this machine accepted `runners`, `submit --repo DIR --runner NAME --wait N -- ARGV`, `status RUNNER ID` and `logs RUNNER ID`.
- The runner now runs caller argv against a working-tree snapshot; it has no registered per-repository `ci` command.
