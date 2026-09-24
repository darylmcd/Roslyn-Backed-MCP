# trace-exception-flow-catch-ranking — Rank trace_exception_flow catches by inheritance distance

**row:** `trace-exception-flow-catch-ranking` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs:172`

## Acceptance

- [ ] trace(PublicInvalidOperationException) lists catch(InvalidOperationException) sites before catch(Exception)
- [ ] countOmitted reported per list

## Evidence

- trace(PublicInvalidOperationException, max 5) → 5× catch(System.Exception) although 9 catch(InvalidOperationException) sites exist. Prior trace-exception-flow-no-throwsite-half partially fixed (throw sites now present). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
