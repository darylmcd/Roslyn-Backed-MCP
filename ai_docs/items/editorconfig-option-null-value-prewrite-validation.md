# editorconfig-option-null-value-prewrite-validation — Investigate null option values before transaction

**row:** `editorconfig-option-null-value-prewrite-validation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/EditorConfigAssignmentValidator.cs:34-46` — earliest null dereference.
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:345-416` — pre-document validation and later write coordinator.
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs:43-51` — stable tool dispatch.
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`
- `tests/RoslynMcp.Tests/EditorConfigArgumentRefusalWireTests.cs`

## Acceptance

- [ ] Reproduce null value through direct service and both supported raw-wire eras; record old error category/identity and file, snapshot, watcher and rollback state.
- [ ] Trace the failing value dereference and every caller before choosing validation ownership. Reject invalid null input before a write transaction when the runtime contract warrants it.
- [ ] Preserve blank/reset and arbitrary valid option values; do not impose the specialized suppression severity enum on the generic option API.
- [ ] Observe regression fail on base and pass after the complete fix. Record ADR and major migration if stable wire identity/category changes.

## Evidence

### Explicit null on the 5.x wire

- Base: `34a87d9c712c19c0af78caca46be5d50af6ab384`.
- Raw `tools/call` with explicit JSON `"value": null` reached the shared validator in protocol `2025-11-25` and `2026-07-28`. Both returned `isError: true`, category `InternalError` and a correlation ID, with no public exception type or schema hint. The production sink correlated each response to `NullReferenceException`.
- Direct service test observed `NullReferenceException` at `EditorConfigAssignmentValidator.Validate` before any coordinator entry. Both protocol probes preserved file bytes, solution/version, watcher state, undo target and change history. Two wire probes passed. The direct regression failed at the expected exception mismatch.
- The typed argument refusal changes the stable wire category. Resume on the next operator-approved major with an ADR, migration note and red-first tests. Use the existing validator before resolving the document or opening the write transaction.
- Machine-gated `dotnet test tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj` selected `SetOptionAsync_NullValue_RefusesBeforeEnteringWriteTransaction` and `SetEditorConfigOption_ExplicitNullValue_RecordsBaseWireAndState`. The base production code yielded the expected direct failure and two wire passes. The enhanced wire-only assertions passed in both protocol eras.
- The earlier line-391 transaction premise was superseded by the pre-transaction assignment validator. Keep blank, reset and arbitrary option values valid.
