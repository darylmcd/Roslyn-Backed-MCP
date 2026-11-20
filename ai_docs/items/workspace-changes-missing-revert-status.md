# workspace-changes-missing-revert-status — Record revert and rollback outcomes in workspace_changes

**row:** `workspace-changes-missing-revert-status` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IChangeTracker.cs:17`
- `src/RoslynMcp.Roslyn/Services/ChangeTracker.cs`

## Acceptance

- [ ] Entries reverted by revert_last_apply/revert_apply_by_sequence show status=reverted
- [ ] apply_with_verify rollbacks show status=rolled_back
- [ ] Path separators normalized
- [ ] validate_workspace auto-scoped changedFilePaths has no separator-only duplicates

## Evidence

- seq 7/28/33 rolled back and 31/35/36 reverted are listed as plain applies; mixed path separators. validate_workspace(changedFilePaths=null) auto-scope lists G4Fixture.cs/G4CacheStore.cs/G4Triangle.cs twice (backslash vs forward-slash) — ChangeTracker stores unnormalized paths; validate_recent_git_changes has no dupes. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Notes

Surface audit 2026-11-20 (server 5.1.1) (finding P6-F5): confirmed on two server sessions. Entries reverted by revert_last_apply, rolled back by apply_with_verify, and a no-op format_document_apply (empty affectedFiles) all stay listed with no marker. Also: preview_multi_file_edit_apply entries carry the preview's description ("Preview multi-file edit..."), add_pragma_suppression and pragma_scope_widen are described as "Apply text edit", and affectedFiles mixes backslash and forward-slash absolute paths.
