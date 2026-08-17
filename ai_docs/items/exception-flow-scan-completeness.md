# exception-flow-scan-completeness — Report exception-flow scan completeness

**row:** `exception-flow-scan-completeness` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Core/Models/ExceptionFlowResult.cs`
- `src/RoslynMcp.Core/Services/IExceptionFlowService.cs`
- `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ExceptionFlowTools.cs`
- `tests/RoslynMcp.Tests/ExceptionFlowServiceTests.cs`

## Acceptance

- [ ] Add `IsComplete` and `FailedDocumentCount` to `ExceptionFlowResult`, distinct from cap-only `CountOmitted`/`Truncated` semantics.
- [ ] Per-tree non-cancellation failures increment the count, retain usable sites, and emit secret-safe correlated diagnostics; replace silent cancellation breaks/null returns with `ThrowIfCancellationRequested` so cancellation propagates.
- [ ] Update the tool description to distinguish scan failures from result-cap omissions.
- [ ] One table-driven scanner-outcome regression covers mixed trees and cancellation, proving retained sites plus incomplete count while `CountOmitted` remains cap-only; complete ordering/caps stay unchanged.
- [ ] Classify the additive public fields under the SDK compatibility decision and changelog policy.

## Evidence

- `ExceptionFlowService` catches per-tree failures and skips the tree.
- Its cancellation polls currently break or return `null`, presenting cancellation as partial/empty success.
- The response reports only cap-driven omissions, so a partial scan is indistinguishable from a complete untruncated result.
