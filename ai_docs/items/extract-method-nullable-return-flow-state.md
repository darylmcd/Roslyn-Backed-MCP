# extract-method-nullable-return-flow-state — Use the returned expression's flow state for extract_method's return type

**row:** `extract-method-nullable-return-flow-state` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:257`

## Acceptance

- [ ] Extracting a region returning a non-null var string yields 'string' (no CS8603)
- [ ] Regression test with nullable context enabled

## Evidence

- extract_method_preview → private string? BuildLine → CS8603 after apply. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
