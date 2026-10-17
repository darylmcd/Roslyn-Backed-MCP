# test-noop-class-cleanup-retirement — Retire no-op fixture cleanup hooks

**row:** `test-noop-class-cleanup-retirement` · **pri:** `Low` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs:180`
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs:8`
- `tests/RoslynMcp.Tests/AliasToolsTests.cs`
- `ai_docs/reports/20261017-test-efficiency/file-inventory.csv`

## Acceptance

- [ ] Use the syntax inventory to split migration into independently reviewable batches; preserve any cleanup that closes workspaces or deletes owned resources.
- [ ] Remove 153 cleanup hooks whose only statement is DisposeServices; remove remaining no-op calls only after preserving surrounding cleanup.
- [ ] Delete the compatibility no-op API after all callers are migrated; retain the single assembly disposal owner and its failure aggregation tests.
- [ ] Verify unchanged discovered behavioral cases and exactly-once assembly cleanup; claim maintainability benefit, not material runtime savings.

## Evidence

- AST scan finds 153 pure no-op ClassCleanup bodies; TestBase.DisposeServices is explicitly a compatibility no-op, not a resource-release operation.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
