# edit-linked-sibling-documents-stale-text — Verify and fix stale text in linked sibling documents after an edit

**row:** `edit-linked-sibling-documents-stale-text` · **pri:** `Low` · **size:** `M` · **verified:** `79ad03bf9291670cb708a6501b1b6297a29f89cf` · **deps:** `edit-syntax-check-project-parse-options`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs`

## Acceptance

- [ ] A linked file in two projects: after apply_text_edit both documents (or the reloaded workspace) show the new text, red-first test; or close the row with evidence that it already holds.

## Evidence

- Plan of edit-syntax-check-project-parse-options (section 9 sketch 4, unverified).
