# test-restore-fixture-serialization — Remove unjustified serialization from private restore-policy fixtures

**row:** `test-restore-fixture-serialization` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadAutoRestoreTests.cs:16`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreBudgetTests.cs:17`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreFailureWireTests.cs:17`

## Acceptance

- [ ] Re-derive ownership of every reachable static/service/environment dependency; each class currently creates private copies and private managers with scripted runners.
- [ ] Remove DoNotParallelize only after proving no shared mutation; retain real budget/cancellation/error-envelope assertions.
- [ ] Run repeatedly alongside shared readers and isolated mutators on Windows/Linux; retain the full suite gate and record serialized-tail reduction.

## Evidence

- Three class-level opt-outs have no dependency explanation; helpers own GUID roots/managers and scripted restore execution, unlike tests that publish global security/recycle state.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
