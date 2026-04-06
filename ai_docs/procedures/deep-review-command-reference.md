# Deep-review command reference

<!-- purpose: Quick command cookbook for importing raw audits, scaffolding rollups, and running the common deep-review batch workflows. -->

Use this file as the **command cookbook** for the multi-repo deep-review workflow.

The actual deep-review prompt run is **client-specific**. There is no single shell command in this repo that invokes `prompts/deep-review-and-refactor.md` across all MCP clients. Start by running that prompt in your MCP client against the target repo, then use the commands below for the shared post-processing steps.

## Prerequisites

Run these from the Roslyn-Backed-MCP repo root unless noted otherwise.

```powershell
Set-Location C:\Code-Repo\Roslyn-Backed-MCP
```

## Common paths

| Purpose | Path pattern |
|---------|--------------|
| Raw audit store | `ai_docs/audit-reports/<timestamp>_<repo-id>_mcp-server-audit.md` |
| Rollup output | `ai_docs/reports/<timestamp>_deep-review-rollup.md` |
| One-command batch helper | `eng/new-deep-review-batch.ps1` |
| Import-only helper | `eng/import-deep-review-audit.ps1` |
| Rollup-only helper | `eng/new-deep-review-rollup.ps1` |
| Lock-mode auto-flip (recommended) | `eng/flip-rw-lock.ps1` |
| Lock-mode force enable rw-lock | `eng/rw-lock-on.ps1` |
| Lock-mode force disable rw-lock (default) | `eng/rw-lock-off.ps1` |
| Lock-mode shared helper (dot-sourced by the three above) | `eng/rw-lock-common.ps1` |

## Concurrency lock-mode launch (Phase 8b)

The deep-review prompt's **Phase 8b** exercises `ROSLYNMCP_WORKSPACE_RW_LOCK`. The flag is bound at server startup and never re-read, so toggling it requires a server restart. Capture the launch command in the raw audit header so the lock mode is auditable.

### Recommended: use `flip-rw-lock.ps1` (auto-detect, one command)

The primary helper is `eng/flip-rw-lock.ps1`. It reads the current User-scope value of `ROSLYNMCP_WORKSPACE_RW_LOCK`, decides the inverse, kills any running `roslynmcp.exe` / `RoslynMcp.Host.Stdio.exe` process, and writes the new value. The operator never has to think about which "side" they are on — the script auto-flips. This is the script the four-step operator dance below uses in step 2.

```powershell
# Auto-detect current mode and flip to the inverse
./eng/flip-rw-lock.ps1

# Dry-run first to see what would be killed and what mode the script will set
./eng/flip-rw-lock.ps1 -WhatIf
```

Two explicit-mode helpers are also available for cases where the operator wants a known target regardless of current state (e.g., the very first run before any User-scope value has been set, or a CI pipeline that needs a deterministic starting mode):

```powershell
# Force mode to rw-lock (sets ROSLYNMCP_WORKSPACE_RW_LOCK=true)
./eng/rw-lock-on.ps1

# Force mode to legacy-mutex (clears the env var)
./eng/rw-lock-off.ps1
```

All three scripts share `eng/rw-lock-common.ps1` (kill-server, set/clear env var, verify, print next steps), accept `-WhatIf`, and never touch generic `dotnet` processes. After running any of them, **fully close and reopen the MCP client** (Claude Code, Cursor, Continue, etc.) so it spawns a fresh server subprocess that inherits the new env var. Restarting just the chat tab is not enough — the client process must exit. The scripts print this reminder at the end of each run.

The helpers are Windows-only. For bash/zsh/macOS/Linux operators, use the manual env-var commands below; the agent's `evaluate_csharp`-based mode detection in Phase 0 step 4 still works regardless of how the env var was set.

### Manual: set the flag for the current shell session (PowerShell)

```powershell
# rw-lock mode (opt-in path under audit)
$env:ROSLYNMCP_WORKSPACE_RW_LOCK = 'true'

# legacy-mutex mode (default — clear the override)
Remove-Item Env:\ROSLYNMCP_WORKSPACE_RW_LOCK -ErrorAction SilentlyContinue
```

### Manual: set the flag for the current shell session (bash / zsh)

```bash
# rw-lock mode (opt-in path under audit)
export ROSLYNMCP_WORKSPACE_RW_LOCK=true

# legacy-mutex mode (default — clear the override)
unset ROSLYNMCP_WORKSPACE_RW_LOCK
```

### Manual: set the flag in the MCP client launch config

Most MCP clients pass an `env` block to the spawned server process. For Claude Code / Cursor / similar clients with a `.mcp.json` (or equivalent) file, add the variable to the `env` of the `roslyn` (or `roslynmcp`) entry:

```jsonc
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "roslynmcp",
      "env": {
        "ROSLYNMCP_WORKSPACE_RW_LOCK": "true"
      }
    }
  }
}
```

After editing the launch config, **restart the MCP server session** (some clients require restarting the entire client). Confirm the new mode in Phase 8b via the parallel-fan-out micro-benchmark.

### Dual-mode deep-review — the four-step flow

**The complete operator dance.** The agent and the helper script handle all bookkeeping, so the operator only does four things:

| # | Operator action | What auto-detection does for you |
|---|-----------------|----------------------------------|
| 1 | **Invoke the deep-review prompt** against the target repo. | Phase 0 step 4 calls `evaluate_csharp` to read `ROSLYNMCP_WORKSPACE_RW_LOCK` from inside the server — that is the canonical lock mode. Phase 0 step 15 globs `ai_docs/audit-reports/` for an opposite-mode partial of the same `<repo-id>`; if none is found, this is run 1. The agent auto-names the audit file `<timestamp>_<repo-id>_<lockMode>_mcp-server-audit.md` and marks Session B columns `skipped-pending-second-run`. |
| 2 | **Run `./eng/flip-rw-lock.ps1`.** | The script reads the current User-scope env var, decides the inverse, kills any running `roslynmcp.exe` / `RoslynMcp.Host.Stdio.exe` process, and writes the new value. No operator decision about which side you are on — the script flips. |
| 3 | **Fully close and reopen the MCP client.** | The next server subprocess spawned by the client inherits the flipped env var. (Restart the chat tab is not enough; the client process must exit.) |
| 4 | **Invoke the deep-review prompt a second time** against the same disposable checkout. | Phase 0 step 4 detects the new lock mode. Phase 0 step 15 finds the run-1 partial in the audit-reports directory and recognises this is run 2 of a pair. Phase 8b.6 inherits the run-1 probe slot definitions, re-runs sub-phases 8b.1, 8b.2, 8b.3, 8b.5 to fill Session B, carries forward run 1's Session A columns into the matrix, and saves the run-2 audit file. The run-2 file is the **canonical dual-mode raw audit** — both sessions are populated in one file. No manual merge step. |

**That is the entire dance.** No operator-supplied filenames. No "decide which mode is run 1". No manual matrix merge. No notes to the agent about which run is which. The four operator inputs are *invoke prompt*, *run script*, *restart client*, *invoke prompt*.

**Single-mode lane** is what happens when the operator runs the prompt only once (skipping steps 2–4). The Session B columns stay `skipped-pending-second-run` in the saved file. A future second run (with the env var flipped) will still pair with it via the auto-pair logic in Phase 0 step 15 — no expiration as long as both files live in `ai_docs/audit-reports/` with matching `<repo-id>`.

**On non-Windows hosts** the helper script is unavailable. Use the manual env-var commands in *Concurrency lock-mode launch* above, then proceed with steps 1, 3, 4 as described. The agent still auto-detects the lock mode via `evaluate_csharp` regardless of how the env var was set.

### Verify the flag took effect

`evaluate_csharp` reads the actual env var inside the running server process, so the deep-review prompt's Phase 0 step 4 already verifies the flag end-to-end: if the flip succeeded, Phase 0 reports the new mode; if it didn't (typo, wrong env block, server reused a stale process), Phase 0 reports the unchanged mode and the audit fails fast.

If you want a quick standalone check without launching a deep-review run, ask the agent to call `evaluate_csharp` with the same script Phase 0 uses:

```csharp
var raw = System.Environment.GetEnvironmentVariable("ROSLYNMCP_WORKSPACE_RW_LOCK");
var parsed = false;
var isOn = bool.TryParse(raw, out parsed) && parsed;
new {
    Raw = raw ?? "(unset)",
    LockMode = isOn ? "rw-lock" : "legacy-mutex"
}
```

The Phase 8b parallel fan-out micro-benchmark also serves as a behavioral fingerprint when needed:

| Mode | Expected speedup for R1 × N (N = `min(4, max(2, logical_cores))`) |
|------|------------------------------|
| `legacy-mutex` | ~1.0× (≤1.3×) — calls serialize behind the per-workspace `SemaphoreSlim` |
| `rw-lock` | between `0.7 × N` and `N` (≥1.6× when N=2; ≥2.5× when N=4) — calls overlap behind the `AsyncReaderWriterLock`, bounded by global throttle |

## Example 1: Raw audit already in the canonical store

Use this when the prompt run happened in this workspace and wrote directly to `ai_docs/audit-reports/`.

```powershell
./eng/new-deep-review-rollup.ps1 `
  -AuditFiles ai_docs/audit-reports/20260406T180100Z_repo-a_mcp-server-audit.md,
              ai_docs/audit-reports/20260406T181500Z_repo-b_mcp-server-audit.md `
  -CampaignPurpose 'Release candidate deep-review batch'
```

## Example 2: Import raw audits produced in external repos

Use this when the prompt wrote fallback raw files into another workspace.

```powershell
./eng/import-deep-review-audit.ps1 `
  -AuditFiles C:\Code-Repo\Repo-A\ai_docs\audit-reports\20260406T180100Z_repo-a_mcp-server-audit.md,
              C:\Code-Repo\Repo-B\ai_docs\audit-reports\20260406T181500Z_repo-b_mcp-server-audit.md
```

## Example 3: One-command import plus rollup scaffold

This is the common external-repo operator path.

```powershell
./eng/new-deep-review-batch.ps1 `
  -AuditFiles C:\Code-Repo\Repo-A\ai_docs\audit-reports\20260406T180100Z_repo-a_mcp-server-audit.md,
              C:\Code-Repo\Repo-B\ai_docs\audit-reports\20260406T181500Z_repo-b_mcp-server-audit.md `
  -CampaignPurpose 'Smoke subset after preview/apply changes'
```

## Example 4: Explicit rollup output path

Use this when you want the scaffold file name chosen up front.

```powershell
./eng/new-deep-review-batch.ps1 `
  -AuditFiles C:\Code-Repo\Repo-A\ai_docs\audit-reports\20260406T180100Z_repo-a_mcp-server-audit.md `
  -OutputPath ai_docs/reports/20260406T190000Z_deep-review-rollup.md `
  -CampaignPurpose 'Experimental-to-stable promotion pass'
```

## Example 5: Overwrite an already-imported raw audit

Use `-Force` only when you intentionally want to replace the canonical raw copy.

```powershell
./eng/import-deep-review-audit.ps1 `
  -AuditFiles C:\Code-Repo\Repo-A\ai_docs\audit-reports\20260406T180100Z_repo-a_mcp-server-audit.md `
  -Force
```

## Example 6: Verify docs after changing the workflow docs

```powershell
./eng/verify-ai-docs.ps1
```

## Recommended operator sequence

| Situation | Command sequence |
|-----------|------------------|
| Prompt run already wrote into `ai_docs/audit-reports/` here | `new-deep-review-rollup.ps1` |
| External repo raw files need to be staged first | `import-deep-review-audit.ps1` → `new-deep-review-rollup.ps1` |
| External repo batch, common path | `new-deep-review-batch.ps1` |

## Helper/background test run pattern

Use this when the MCP client supports subagents, background agents, or another background execution facility. The exact helper command is client-specific, so structure the run like this rather than trying to standardize one shell wrapper here.

1. In the **primary agent**, keep workspace setup, repo-shape checks, and test selection (`test_discover`, `test_related_files`, `test_related`). Decide exactly which heavy validations need to run.
2. Send only the heavy validation step to the helper/background worker: filtered `test_run`, full-suite `test_run`, `test_coverage`, or an equivalent fallback shell test command if the client cannot run the MCP test tools directly.
3. Point the helper/background worker at the **same repo, branch/worktree, and disposable checkout** as the primary agent. Do not delegate preview/apply chains or other workspace-version-sensitive mutation steps.
4. Require the helper/background worker to return a **summary only**: tool or command used, filter or scope, pass/fail counts, failing test names, approximate duration, coverage headline, and any anomalies or client/tool errors.
5. Back in the **primary agent**, convert that summary into PASS / FLAG / FAIL judgments, update the coverage ledger, and record any helper/runtime constraints in the raw audit report.

### Example operator prompt

Use or adapt this when your client can launch a subagent or background worker:

```text
Run a helper/background validation pass in the same repo, branch/worktree, and disposable checkout as the primary deep-review audit.

Constraints:
- Do not change code, run preview/apply mutations, or alter workspace state beyond the requested test/build execution.
- Do not paste raw logs unless execution fails so badly that a short summary is impossible.

Tasks:
1. Run `test_run` with filter `<related-test-filter>`.
2. Run full-suite `test_run` with no filter.
3. Run `test_coverage`.
4. If the MCP test tools are unavailable in this client, use the approved fallback shell test command for the repo instead.

Return summary only:
- tool or command used for each step
- filter or scope
- pass/fail/skipped counts
- failing test names
- approximate duration per step
- coverage headline
- anomalies, timeouts, or client/tool errors
```

## Related

- `deep-review-program.md` — full workflow, repo matrix, and backlog intake rules
- `../audit-reports/README.md` — raw audit storage rules
- `../reports/README.md` — rollup rules and example report
- `../../eng/import-deep-review-audit.ps1` — import helper
- `../../eng/new-deep-review-rollup.ps1` — rollup scaffold helper
- `../../eng/new-deep-review-batch.ps1` — one-command batch helper