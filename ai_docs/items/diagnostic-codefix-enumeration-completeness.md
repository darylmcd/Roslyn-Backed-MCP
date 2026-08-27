# diagnostic-codefix-enumeration-completeness — Distinguish failed code-fix enumeration

**row:** `diagnostic-codefix-enumeration-completeness` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,code-action-provider-loading-consolidation`

## Anchors

- `src/RoslynMcp.Core/Models/DiagnosticDetailsDto.cs`
- `src/RoslynMcp.Roslyn/Contracts/ICodeFixProviderRegistry.cs`
- `src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`
- `tests/RoslynMcp.Tests/CodeFixProviderRegistryTests.cs`

## Acceptance

- [ ] Add a detailed registry projection carrying providers, load completeness, and failed-load count from the shared provider loader; carry `FixEnumerationComplete` and the combined `FailedProviderCount` into `DiagnosticDetailsDto`.
- [ ] Keep `GetProvidersFor` / `FirstProviderFor` as compatibility projections over that detailed result, but fail closed when an incomplete load would otherwise be interpreted as provider absence.
- [ ] Keep actions from healthy providers when another provider fails; do not turn a provider crash into the false guidance that no provider is loaded.
- [ ] Propagate cancellation and emit one secret-safe correlated diagnostic per failed provider without raw exception message, provider path, or secret-bearing value.
- [ ] One table-driven provider-outcome regression covers healthy, throwing, mixed, and canceled enumeration; complete results and existing actionable fallbacks remain unchanged.
- [ ] Classify the additive public fields and corrected guidance under the SDK compatibility decision and changelog policy.

## Evidence

- `CaptureRegisteredActionsAsync` catches every non-cancellation provider exception and returns an empty action list.
- `CodeFixProviderRegistry` also drops assembly/type/constructor failures before `DiagnosticService` can observe them.
- `GetFixGuidance` then tells consumers that no code-fix provider is loaded even when a loaded provider actually failed.
