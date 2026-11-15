# persistence-null-filepath-silent-skip — Stop silently skipping null-FilePath documents at persistence

**row:** `persistence-null-filepath-silent-skip` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:215` — added documents skipped when FilePath is null (also :241 changed, :260 removed).

## Acceptance

- [ ] A reviewed added/changed/removed document with a null FilePath is refused or reported, never skipped silently; test through the persistence entry point.
- [ ] Release-impact assessment recorded before choosing refuse vs report (same class as rebase-changed-document-silent-drop: apply would fail where it reported success).

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): persistence-stage silent drop of a reviewed effect, independent of the rebase stage.

## Notes

5.x screen at d9e2b3ff. PersistAddedDocumentsAsync, PersistChangedDocumentsAsync and PersistRemovedDocumentsAsync skip null FilePath. RefactoringService uses that persistence path from stable apply_code_action as well as experimental routes. Select when a stable-wire fixture establishes that the complete refusal or receipt correction fits 5.x, or at the next operator-approved major.
