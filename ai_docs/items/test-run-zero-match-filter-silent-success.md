# test-run-zero-match-filter-silent-success — Warn when a test_run filter matches zero tests

**row:** `test-run-zero-match-filter-silent-success` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`

## Acceptance

- [ ] test_run(filter matching nothing) returns a warning (or distinct status) naming the filter

## Evidence

- test_run(filter=NoMatch, compact) → total 0, succeeded true, no warning. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
