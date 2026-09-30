# editorconfig-write-outside-workspace — Confine config writes to loaded workspace files

**row:** `editorconfig-write-outside-workspace` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:333-342`
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs`

## Acceptance

- [ ] Reject a source file path that is not a document in the selected loaded workspace before creating or changing any .editorconfig file.
- [ ] An alias into a sibling directory is rejected by physical path identity; a valid loaded document remains writable.
- [ ] Regression proves an outside path leaves the destination and workspace state unchanged.

## Evidence

- Code inspection 2026-09-29: SetOptionAsync checks that workspaceId exists but chooses the config path from an arbitrary caller-supplied sourceFilePath. EditorConfigTools forwards that path unchanged, permitting an out-of-workspace config write.

## Context

- Separate from stale diagnostics after a valid .editorconfig write; validate the target before config I/O.
