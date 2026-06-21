# workspace-unresolved-analyzer-message-wording — reword the WORKSPACE_UNRESOLVED_ANALYZER diagnostic

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (the `WORKSPACE_UNRESOLVED_ANALYZER` diagnostic message text, ~`:1503` pre-#1009)

## Acceptance

- [ ] The diagnostic message no longer implies a restore remedy ("unresolved package path") for a condition that now routes exclusively to `BuildRequired`; lead with the `dotnet build` remedy.

## Evidence

After PR #1009 the `WORKSPACE_UNRESOLVED_ANALYZER` warning unconditionally sets `BuildRequired=true`/`RestoreRequired=false`, but its message still names "an unresolved package path" (a restore framing). Genuine package-restore drift is detected separately by `DetectRestoreRequired`. Source: 2026-06-21 backlog-sweep code-quality review of PR #1009 (severity: low).

## Context

Pure docs/wording fix; one diagnostic message string.
