# find-dead-locals-captured-by-local-function — Skip outer locals in find_dead_locals' per-local-function data-flow pass

**row:** `find-dead-locals-captured-by-local-function` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1247`

## Acceptance

- [ ] WorkspaceReloadedEventTests.cs:60 'observed' is not reported
- [ ] Regression test with a local function writing a captured outer local later read by the outer method

## Evidence

- 4 of 5 hits are false positives: outer local captured by a local function (containingMethod=ObservingHandler). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
