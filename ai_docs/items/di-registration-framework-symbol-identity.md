# di-registration-framework-symbol-identity — Bind DI discovery to framework symbols

**row:** `di-registration-framework-symbol-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:351-355`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs`

## Acceptance

- [ ] Recognize supported DI registrations through bound framework method/type identity rather than simple receiver or containing-type names.
- [ ] Preserve supported framework extension/static invocation and lifetime results; reject user-defined IServiceCollection and similarly named ServiceCollection types/methods.
- [ ] Observe red-first public discovery regressions with real framework references and unrelated same-name controls.

## Evidence

- Main 0e0f90661035a38e570627b1508dac2ea2496708 uses receiver.Name/AllInterfaces.Name and ContainingType.Name.Contains to admit registrations; current-session semantic source inspection confirms the name-only mechanism. No runtime reproduction claimed.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.

## Notes

On 9e7a9439, DiRegistrationService.cs:351–355 admits by receiver/interface simple name or containing type name. Stable get_di_registrations exposes those matches in counts, pages and lifetime chains (AdvancedAnalysisTools.cs:75 onward). The row may remove real container registrations alongside false positives. Qualify supported framework/static calls, true custom registrations and ordinary lookalikes through public MCP before selection. If supported results must be removed, reopen with the next approved major and migration coverage.
