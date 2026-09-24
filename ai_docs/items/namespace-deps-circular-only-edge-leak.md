# namespace-deps-circular-only-edge-leak — Filter circularOnly by edge-on-cycle, not endpoint-in-cycle

**row:** `namespace-deps-circular-only-edge-leak` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:316`

## Acceptance

- [ ] circularOnly=true returns only edges within a strongly connected component

## Evidence

- circularOnly=true returns Host.Stdio.Tools→Core.Models (34), which is on no cycle. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
