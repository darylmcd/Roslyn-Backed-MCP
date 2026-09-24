# scaffold-type-mixed-line-endings — Use one newline convention in TypeScaffolder stub assembly

**row:** `scaffold-type-mixed-line-endings` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeScaffolder.cs:192`

## Acceptance

- [ ] Scaffolded file has a single EOL style matching .editorconfig end_of_line

## Evidence

- scaffold_type_apply → G6Widget.cs LF lines with 2 CRLF blank separators. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
