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

## Concurrency model

The server gates each workspace with a per-workspace `Nito.AsyncEx.AsyncReaderWriterLock`. Reads against the same workspace run concurrently (bounded only by the global throttle); writes are exclusive against all other operations on the same workspace. There is no opt-out and no environment variable to flip — Phase 8b of the deep-review prompt benchmarks parallel fan-out behaviour against this single mode.

| Probe | Expected wall-clock for R1 × N (N = `min(4, max(2, logical_cores))`) |
|-------|------------------------------|
| Parallel reads, same workspace | between `0.7 × N` and `N` (≥1.6× when N=2; ≥2.5× when N=4) — calls overlap on the per-workspace RW lock, bounded by global throttle |

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