# set-editorconfig-option-creates-parallel-section — Reuse an existing matching section in set_editorconfig_option

**row:** `set-editorconfig-option-creates-parallel-section` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397`

## Acceptance

- [ ] Setting a C# key on an .editorconfig that already has a C#-files section edits that section instead of appending a parallel multi-extension glob section
- [ ] get_editorconfig_options reflects the value

## Evidence

- set_editorconfig_option added [*.{cs,csx,cake}] next to the existing [*.cs] section (Phase 6/7). Related prior #735 (duplicate key) — no duplicate key this run. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
