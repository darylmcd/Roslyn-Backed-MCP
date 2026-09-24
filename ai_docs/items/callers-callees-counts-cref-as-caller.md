# callers-callees-counts-cref-as-caller — Exclude doc-comment cref references from callers_callees

**row:** `callers-callees-counts-cref-as-caller` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:367`

## Acceptance

- [ ] callers of ICompilationCache.GetCompilationAsync exclude ICompilationCache.cs:32,101 crefs

## Evidence

- callers include `<see cref="GetCompilationAsync"/>` at ICompilationCache.cs:32,101. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
