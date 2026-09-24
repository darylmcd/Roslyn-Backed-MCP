# consumer-analysis-cancellation-returns-partial — Throw on cancellation in ConsumerAnalysisService

**row:** `consumer-analysis-cancellation-returns-partial` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:51`

## Acceptance

- [ ] Cancellation surfaces as OperationCanceledException / cancelled envelope, never a partial success

## Evidence

- Code read during Phase 3 (bad-code sighting, Directive #3). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
