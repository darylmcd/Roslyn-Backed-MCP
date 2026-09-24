# flow-analysis-silent-region-narrowing — Report the effective range (or refuse) when flow analysis narrows a multi-member selection

**row:** `flow-analysis-silent-region-narrowing` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:247`

## Acceptance

- [ ] A range spanning two methods returns a warning + effective range (or InvalidArgument)
- [ ] Single-method ranges unchanged
- [ ] Regression test

## Evidence

- analyze_control_flow(MutationAnalysisService.cs 667-725) → analyzed only 711-725 (next method); returns 672/675 dropped, no warning. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
