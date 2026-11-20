# project-diagnostics-totals-collapse-diagnostic-id — Keep project_diagnostics totals invariant under diagnosticId

**row:** `project-diagnostics-totals-collapse-diagnostic-id` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`

## Acceptance

- [ ] `project_diagnostics(diagnosticId=CA2016)` reports the same total* values as the unfiltered call, with filteredDiagnostics narrowed; or the schema text is corrected if narrowing is intended. Decide which against the stable contract.
- [ ] Reconcile with fix_all_preview, which reported 224 CA1826 occurrences where this tool reported 28.

## Evidence

- G1-F1: totals 7442 became 1 and 14 under diagnosticId; severity filter preserved 7442.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
