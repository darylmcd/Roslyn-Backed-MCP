# netanalyzers-package-duplicates-sdk-analyzers — Drop the redundant Microsoft.CodeAnalysis.NetAnalyzers package reference

**row:** `netanalyzers-package-duplicates-sdk-analyzers` · **pri:** `Medium` · **size:** `S`

## Anchors

- `Directory.Build.props:27`

## Acceptance

- [ ] Only one NetAnalyzers copy is loaded (list_analyzers analyzer count drops; csc SARIF has no duplicate CA results)
- [ ] project_diagnostics CA counts halve (e.g. CA1822 64 → 32 on the sample)
- [ ] CI analyzer severity gates still pass

## Evidence

- csc SARIF build of SampleLib: 74 results / 38 distinct; CA1822 64 = 32 unique ×2; list_analyzers 10 analyzers vs 6 assemblies. Package 10.0.401 ships no build/ props to disable the SDK copy (EnableNETAnalyzers defaults true). — see `ai_docs/audits/20260924-1305/report.md` (check C6) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Repo defect, not a server bug. Alternative: keep the package and set EnableNETAnalyzers=false. Optional server hardening tracked separately in compilation-cache-dedupe-analyzer-references.
