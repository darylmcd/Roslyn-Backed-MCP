# passed-trx-fixture-summary

**row:** `passed-trx-fixture-summary` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs:845-869` — WritePassedTrxFixture has passed result/counters and failed summary.
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs:475-505` — successful invocation report control expects no failure envelope.

## Acceptance

- [ ] Make the passed fixture internally consistent: passed result, counters and ResultSummary.
- [ ] Keep actual unsuccessful report-summary classification strict; preserve separate nonzero-process-with-passed-TRX and explicit failed-summary controls.
- [ ] Retain all invocation report collection/envelope/paging/wire assertions and ownership.
- [ ] Run the real selected report/envelope contracts before merged closeout.

## Evidence

| Source inspection 2026-10-23 | Observation |
|---|---|
| Independent full reader | Parser intentionally maps Failed summary to unsuccessfulReport and Unknown failure for successful execution; fixture contradicts its successful-report assertions. |
| Limits | No parser relaxation or actual runtime result claimed; test-source repair/native qualification pending. |
