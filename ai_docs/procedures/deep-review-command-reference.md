# Deep-review command reference

<!-- purpose: Operator commands for the multi-repo deep-review pipeline. -->

Run from the **Roslyn-Backed-MCP** repo root:

```powershell
Set-Location C:\Code-Repo\Roslyn-Backed-MCP
```

## One command (recommended)

**Import sibling audits → canonical store → rollup → backlog merge**

```powershell
./eng/new-deep-review-batch.ps1
```

**Defaults**

- Scans **sibling git checkouts** under the parent of this repo (e.g. `C:\Code-Repo\*` when this repo is `C:\Code-Repo\Roslyn-Backed-MCP`).
- Excludes only this repository’s folder name from sibling scans (so other repos’ audits are pulled in).
- **Overwrites** canonical audit files when the external copy is newer (use `-NoOverwrite` to disable).
- Updates **`ai_docs/backlog.md`** unless you pass `-SkipBacklogSync`.

**Dry-run backlog only**

```powershell
./eng/new-deep-review-batch.ps1 -BacklogWhatIf
```

**Explicit audit paths (no sibling scan)**

```powershell
./eng/new-deep-review-batch.ps1 -AuditFiles @(
  'C:\Code-Repo\IT-Chat-Bot\ai_docs\audit-reports\20260408T200500Z_itchatbot_mcp-server-audit.md'
)
```

**Custom rollup path / label**

```powershell
./eng/new-deep-review-batch.ps1 -OutputPath ai_docs/reports/manual_rollup.md -CampaignPurpose 'RC sign-off'
```

## Optional helpers

| Script | Role |
|--------|------|
| `eng/import-deep-review-audit.ps1` | Import only (when not using the batch). |
| `eng/new-deep-review-rollup.ps1` | Rollup scaffold only. |
| `eng/compare-external-audit-sources.ps1` | Optional pre-flight compare (batch already refreshes imports by default). |
| `eng/sync-deep-review-backlog.ps1` | Backlog merge only (normally invoked by the batch). |

## Verify AI docs links

```powershell
./eng/verify-ai-docs.ps1
```

## Related

- `deep-review-program.md` — coverage matrix and program rules
- `deep-review-backlog-intake.md` — backlog automation details
- `../audit-reports/README.md` — raw audit storage
- `../reports/README.md` — rollup storage
