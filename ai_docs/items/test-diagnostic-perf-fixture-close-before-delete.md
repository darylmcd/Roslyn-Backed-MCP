# test-diagnostic-perf-fixture-close-before-delete — Close the diagnostic performance fixture before deleting it

**row:** `test-diagnostic-perf-fixture-close-before-delete` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/DiagnosticServicePerfTests.cs:36`
- `tests/RoslynMcp.Tests/DiagnosticServicePerfTests.cs:47`

## Acceptance

- [ ] Close the private loaded workspace before deleting its root; preserve independent cleanup attempts and aggregate failures.
- [ ] Cover initialization failure and cleanup ownership; prove the workspace is absent and directory removed after class cleanup.
- [ ] Preserve cold-load/reload timing assertions; do not remove serialization merely to accelerate the suite.

## Evidence

- Class initialization loads a private copy into the assembly manager, but cleanup deletes its directory and calls a no-op; the session remains until assembly cleanup.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
