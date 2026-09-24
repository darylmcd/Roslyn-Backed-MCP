# tool-output-schema-coverage — Add outputSchema + structuredContent to the high-traffic read tools

**row:** `tool-output-schema-coverage` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`

## Acceptance

- [ ] The named tools declare outputSchema and return structuredContent that validates
- [ ] Schema drift test covers them

## Evidence

- tools/list: outputSchema on 8 tools (server_*, workspace_* status family), all 8 validate clean; 167 return JSON as text only. — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`
