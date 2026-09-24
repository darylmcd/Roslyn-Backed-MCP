# bulk-replace-type-redundant-using — Skip redundant usings in bulk_replace_type_preview

**row:** `bulk-replace-type-redundant-using` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:114`

## Acceptance

- [ ] No 'using SampleLib;' added inside namespace SampleLib

## Evidence

- bulk_replace_type_preview adds 'using SampleLib;' inside namespace SampleLib. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
