# mcp-surface-audit-2026-09-24-info-digest — Triage the 2026-09-24 surface-audit Info digest

**row:** `mcp-surface-audit-2026-09-24-info-digest` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs`

## Acceptance

- [ ] Each item below is either confirmed and split into its own row, or closed with a reason
- [ ] Unconfirmed: symbol_search totalCount caps at exactly 1000 on broad queries (clamp?)
- [ ] Unconfirmed: member_hierarchy lists no implementations for an interface member
- [ ] Unconfirmed: symbol_impact_sweep mapperCallsites flags a *Serializer parameter reference
- [ ] Unconfirmed: compile_check lists syntax errors twice; project_diagnostics total* honor the diagnosticId filter although the schema says totals ignore filters
- [ ] Info: find_overloads empty memberName → silent empty; test_coverage missingPackages holds project names; project_diagnostics rows unsorted within a file

## Evidence

- Recorded as Info or unconfirmed by phase runners G2/G4/G5 and the closure probe; see findings.json (severity Info) and report § Info. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
