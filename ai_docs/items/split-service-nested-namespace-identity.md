# split-service-nested-namespace-identity — Preserve the full namespace chain

**row:** `split-service-nested-namespace-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:1324`
- `tests/RoslynMcp.Tests/SplitServiceReceivingNamespaceIntegrationTests.cs` (extend)

## Acceptance

- [ ] Public split-service preview/apply for a type under nested namespace declarations emits partitions in the original full namespace.
- [ ] Compile and bind serialized preview and applied/reloaded output, retaining source and unrelated bindings.

## Evidence

- Cold plan review on 2026-10-31: `GetNamespaceName` selects only the nearest namespace declaration before partition emission.
