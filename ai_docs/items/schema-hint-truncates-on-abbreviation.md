# schema-hint-truncates-on-abbreviation — Stop TrimToFirstSentence from splitting on abbreviations

**row:** `schema-hint-truncates-on-abbreviation` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:788`

## Acceptance

- [ ] recommend_workflow(task='') schemaHint contains the full example
- [ ] evaluate_csharp hint keeps 'Must be > 0'

## Evidence

- recommend_workflow(task='') → schemaHint '…Natural-language task, e.g)'. — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`
