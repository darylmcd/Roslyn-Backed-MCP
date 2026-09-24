# format-range-refuses-on-unrelated-line-count-change — Format only the requested span in format_range_preview

**row:** `format-range-refuses-on-unrelated-line-count-change` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1197`

## Acceptance

- [ ] format_range_preview(60-80) succeeds when an unrelated blank line at line 8 would be removed by full-document format
- [ ] Edits stay inside the requested range
- [ ] Regression test

## Evidence

- format_range_preview range 60-80 refused because of a blank line at line 8. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
