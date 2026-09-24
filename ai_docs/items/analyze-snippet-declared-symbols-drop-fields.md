# analyze-snippet-declared-symbols-drop-fields — Collect field variables and other member kinds in analyze_snippet declaredSymbols

**row:** `analyze-snippet-declared-symbols-drop-fields` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:105`

## Acceptance

- [ ] kind=members snippet with `private string _name` lists the field
- [ ] Enums/events/ctors listed

## Evidence

- analyze_snippet(kind=members) → CS0414 on _name but declaredSymbols lacks it. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
