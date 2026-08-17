# di-registration-scan-completeness — Report DI scan completeness

**row:** `di-registration-scan-completeness` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration,reflection-usage-scan-completeness`

## Anchors

- `src/RoslynMcp.Core/Models/DiRegistrationDto.cs`
- `src/RoslynMcp.Core/Services/IDiRegistrationService.cs`
- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`
- `tests/RoslynMcp.Tests/DiLifetimeOverrideTests.cs`

## Acceptance

- [ ] Extend `DiRegistrationScanResult` and the cached snapshot with `IsComplete` plus `FailedDocumentCount`.
- [ ] Every `get_di_registrations` branch uses the detailed scan and emits both fields; when incomplete, document `totalCount` as an observed lower bound.
- [ ] Keep `GetDiRegistrationsAsync` for mutation consumers, but fail closed on an incomplete cached scan so registration absence is never inferred from partial data.
- [ ] Per-tree non-cancellation failures increment the count and emit secret-safe correlated diagnostics; replace silent cancellation breaks/returns with `ThrowIfCancellationRequested` so cancellation propagates.
- [ ] One table-driven scanner-outcome regression covers mixed trees and cancellation, proving partial tool output, legacy-projection refusal, and unchanged complete caching/reference behavior.
- [ ] Classify the additive public fields under the SDK compatibility decision and changelog policy.

## Evidence

- `DiRegistrationService` catches per-tree failures, logs, and continues while caching the reduced registration set.
- Its cancellation polls currently break or return a partial result instead of propagating cancellation.
- Public totals and mutation consumers currently treat that partial cache as complete, which can hide registrations and produce unsafe absence decisions.
