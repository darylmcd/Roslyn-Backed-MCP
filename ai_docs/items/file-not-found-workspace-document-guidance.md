# file-not-found-workspace-document-guidance — Distinguish workspace misses from disk misses

**row:** `file-not-found-workspace-document-guidance` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:103`

## Acceptance

- [ ] A source document absent from the loaded workspace gets workspace-membership guidance even when its producer throws `FileNotFoundException`.
- [ ] A genuinely missing disk file retains disk-path guidance.
- [ ] Public messages reveal no caller path or host filesystem detail; preserve the existing wire category unless an explicit contract decision changes it.
- [ ] Tests cover the observed producers and the post-reload retry behavior.

## Evidence

- During the 2026-11-05 document-miss remediation, `SymbolSearchService.cs:231`, `EditorConfigService.cs:47,359`, `FixAllTargetResolver.cs:54`, `FlowAnalysisService.cs:204`, and `OperationService.cs:23` were found to throw `FileNotFoundException` for a document absent from the workspace, but the shared public handler advises callers to verify that the file exists on disk.
