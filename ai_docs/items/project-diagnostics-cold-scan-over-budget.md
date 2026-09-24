# project-diagnostics-cold-scan-over-budget — Cut project_diagnostics cold cost and default payload

**row:** `project-diagnostics-cold-scan-over-budget` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs:70`

## Acceptance

- [ ] Default response < 100 KB on the 905-doc solution
- [ ] Cold elapsed documented or reduced below 15 s

## Evidence

- project_diagnostics(limit=20) cold elapsedMs=24902 (budget 15 s); default limit=200 ≈190 KB; diagnostics resource cold 19.3 s. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
