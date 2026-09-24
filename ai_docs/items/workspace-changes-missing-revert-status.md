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
