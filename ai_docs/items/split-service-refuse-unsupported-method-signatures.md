# split-service-refuse-unsupported-method-signatures — Refuse split_service_with_di_preview for method shapes the stub cannot forward

**row:** `split-service-refuse-unsupported-method-signatures` · **pri:** `Medium` · **size:** `M` · **deps:** `split-service-refuse-cross-partition-references`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:1254`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] Moving a `static`, `override`/`abstract`, explicit-interface, `ref`-returning or generic-with-constraints method, or splitting a generic source type, is refused with a specific reason (or correctly forwarded where the stub can be exact).
- [ ] `BuildForwardingMethod` no longer drops `ExplicitInterfaceSpecifier` or constraint clauses silently, and a static stub never references the instance partition field.
- [ ] Regression test: each shape either refuses or yields a preview with zero new `compile_check` errors.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6; 2026-09-25 ctor-injection-precision review). Pre-existing on `main`: `BuildPartitionFile` (`SymbolRefactorService.cs:768`) emits only moved methods plus instance fields; `BuildForwardingMethod` (`:1254`) builds forwarding stubs. The defects are visible (non-compiling previews), not silent, hence Medium.

## Context

- Split child of `split-service-with-di-refuse-unsupported-method-shapes` (plan 20261001T034545Z; one regression shape each). Siblings share `SymbolRefactorService.cs` and `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`, so they are chained by `deps`. Each ships independently: a refusal or a correct forward, never a half-supported shape.
