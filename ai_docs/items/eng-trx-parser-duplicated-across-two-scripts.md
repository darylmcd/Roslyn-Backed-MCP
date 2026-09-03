# eng-trx-parser-duplicated-across-two-scripts — Extract the duplicated MSTest TRX reader

**row:** `eng-trx-parser-duplicated-across-two-scripts` · **pri:** `Low` · **size:** `M` · **deps:** `—`

## Anchors

- `eng/summarize-test-results.ps1`
- `eng/collect-hosted-shard-timings.ps1`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] Both scripts dot-source one TRX reader; the hardened `XmlReaderSettings` setup, the definition-id dedupe, the result-to-definition join check, and the duration parsing exist exactly once.
- [ ] The malformed-input rethrow names the offending file path and preserves the inner exception message in the shared implementation.
- [ ] Existing exact-string regressions in `TestResultsSummaryContractTests` are updated in step, and one regression proves the shared guard fires for both consumers.

## Evidence

Diffed line by line by the cold reviewer of PR #1427: `Read-TrxCaseDuration` in
`eng/collect-hosted-shard-timings.ps1` is a near-verbatim clone of `Read-TrxCases` in
`eng/summarize-test-results.ps1` — identical `DtdProcessing::Prohibit` plus null-`XmlResolver`
setup, identical `TestDefinitions`/`UnitTestResult` join validation, identical duration parsing and
negative-duration guard. The collector aggregates ticks instead of yielding case objects; everything
else is the same. A future XML-hardening fix would land in one copy only.

## Context

The mirror was requested by the `ci-hosted-shard-duration-balancing` stanza, so it was not a scope
violation in PR #1427. De-duplicating it there would have pulled in a fifth production file and
broken the Rule 3 cap, so it was correctly left for its own row.

PR #1427 already closed the dangerous half of the duplication (an unguarded Markdown appender that
omitted the sibling's input-clobber guard) by deleting the unrequested `-OutputPath` parameter
outright. The parser half remains. Follow the existing `eng/format-diagnostic-contract.ps1`
dot-sourced-helper precedent.

[source: 2026-09-02 backlog-remediate PR #1427 cold review + executor Directive #3 report]
