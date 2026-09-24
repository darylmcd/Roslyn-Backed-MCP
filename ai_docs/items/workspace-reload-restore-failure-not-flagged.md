# workspace-reload-restore-failure-not-flagged — Set restoreRequired when project.assets.json carries restore errors

**row:** `workspace-reload-restore-failure-not-flagged` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`

## Acceptance

- [ ] After an NU1201 restore failure, workspace_reload reports restoreRequired=true with a restoreHint

## Evidence

- workspace_reload after NU1201 → workspaceErrorCount 1, isReady false, restoreRequired false, restoreHint null. Root cause not traced — confirm anchor during deepening. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
