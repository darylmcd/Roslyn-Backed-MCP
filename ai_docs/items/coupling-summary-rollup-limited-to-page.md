# coupling-summary-rollup-limited-to-page — Compute get_coupling_metrics project rollups over all types

**row:** `coupling-summary-rollup-limited-to-page` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CouplingAnalysisTools.cs:33`

## Acceptance

- [ ] summary=true (default limit 100) reports totalTypes 1504 and 6 projects on W_RO
- [ ] Regression test with limit < type count

## Evidence

- summary=true → totalTypes=100, projectCount=3, Core absent; limit=5000 → 1504 types, 6 projects. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
