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

## Related

- `deep-review-program.md` — full workflow, repo matrix, and backlog intake rules
- `../audit-reports/README.md` — raw audit storage rules
- `../reports/README.md` — rollup rules and example report
- `../../eng/import-deep-review-audit.ps1` — import helper
- `../../eng/new-deep-review-rollup.ps1` — rollup scaffold helper
- `../../eng/new-deep-review-batch.ps1` — one-command batch helper