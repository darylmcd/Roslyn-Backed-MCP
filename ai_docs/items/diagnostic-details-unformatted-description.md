# diagnostic-details-unformatted-description — Format diagnostic_details description with the diagnostic's arguments

**row:** `diagnostic-details-unformatted-description` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:132`

## Acceptance

- [ ] CS0414 description names the field

## Evidence

- CS0414 → description "The field '{0}' is assigned but its value is never used". — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
