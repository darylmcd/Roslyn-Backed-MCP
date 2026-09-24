# workspace-status-verbose-not-superset — Make status/verbose a superset of status

**row:** `workspace-status-verbose-not-superset` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs:64`

## Acceptance

- [ ] status/verbose keys ⊇ status keys

## Evidence

- status has isReady/analyzersReady/restoreHint/solutionFileName/workspaceErrorCount; status/verbose has none of them. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
