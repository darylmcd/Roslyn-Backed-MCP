# code-action-provider-loading-consolidation — Consolidate feature-provider loading

**row:** `code-action-provider-loading-consolidation` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration,fixall-provider-error-detail-redaction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`
- `src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs`
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs`
- `src/RoslynMcp.Roslyn/Services/CSharpFeatureProviderLoader.cs` (new)
- `tests/RoslynMcp.Tests/CodeActionServiceTests.cs`
- `tests/RoslynMcp.Tests/CodeFixProviderRegistryTests.cs`
- `tests/RoslynMcp.Tests/FixAllServiceTests.cs`

## Acceptance

- [ ] Single-source CSharp.Features code-fix/refactoring provider discovery and construction for CodeActionService, CodeFixProviderRegistry, and FixAllService without a service locator or duplicated reflection loop.
- [ ] Preserve every healthy provider while distinguishing no parameterless constructor, constructor failure, type-load failure, and assembly-load failure; do not label every skipped candidate as constructor-shape debt.
- [ ] Propagate cancellation and emit one secret-safe structured diagnostic/count for each expected failure class without raw exception messages or paths.
- [ ] One table-driven loader-outcome regression drives healthy, no-constructor, throwing-constructor, and type-load cases; all three consumers use the same result model.
- [ ] Keep public code-action/fix-provider behavior unchanged for healthy assemblies.

## Evidence

- `CodeActionService` independently reflects code-fix and refactoring providers, catches every non-cancellation constructor exception, and silently returns `null`.
- `CodeFixProviderRegistry` duplicates the code-fix loop, silently drops constructor failures, then reports every skipped candidate as lacking a parameterless constructor.
- `FixAllService` contains a third reflection/construction loop with the same silent constructor-failure and misleading skipped-count behavior.
