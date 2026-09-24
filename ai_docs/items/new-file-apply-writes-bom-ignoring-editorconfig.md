# new-file-apply-writes-bom-ignoring-editorconfig — Route new-file writes through SourceFileEncoding's no-BOM default

**row:** `new-file-apply-writes-bom-ignoring-editorconfig` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1031`

## Acceptance

- [ ] create_file_apply / move_type_to_file_apply / scaffold_*_apply honor .editorconfig charset and end_of_line
- [ ] All apply paths agree on encoding

## Evidence

- move_type_to_file_apply → EF BB BF + CRLF with .editorconfig end_of_line=lf charset=utf-8; apply_composite writes no BOM but CRLF. Distinct from composite-apply-encoding-hygiene-consolidated (test/doc hygiene). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
