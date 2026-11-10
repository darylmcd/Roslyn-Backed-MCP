# persistence-null-filepath-silent-skip — Stop silently skipping null-FilePath documents at persistence

**row:** `persistence-null-filepath-silent-skip` · **pri:** `Medium` · **size:** `S` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:215` — added documents skipped when FilePath is null (also :241 changed, :260 removed).

## Acceptance

- [ ] A reviewed added/changed/removed document with a null FilePath is refused or reported, never skipped silently; test through the persistence entry point.
- [ ] Release-impact assessment recorded before choosing refuse vs report (same class as rebase-changed-document-silent-drop: apply would fail where it reported success).

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): persistence-stage silent drop of a reviewed effect, independent of the rebase stage.
