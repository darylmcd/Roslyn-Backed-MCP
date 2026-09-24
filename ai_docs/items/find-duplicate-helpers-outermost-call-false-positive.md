# find-duplicate-helpers-outermost-call-false-positive — Require parameter forwarding before find_duplicate_helpers reports a re-wrap

**row:** `find-duplicate-helpers-outermost-call-false-positive` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:784`

## Acceptance

- [ ] BuildEntries => GetParameters().Where().Select().ToArray() is not reported
- [ ] A true re-wrap (x => Enumerable.ToArray(x)) still is
- [ ] False-positive rate on the 10-sample spot check ≤ 10%

## Evidence

- ≥5 of 10 spot-checked hits (BuildEntries, FormatIdentityDrift, AppendRow, FormatVersionCheckStatus…) are false positives, all confidence=high. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
