# split-service-refuse-cross-partition-references — Refuse split_service_with_di_preview when a moved method references members it will not carry

**row:** `split-service-refuse-cross-partition-references` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:768`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] `split_service_with_di_preview` refuses with a specific reason naming the moved method and the referenced member when a moved method references a kept method, property, static member, base member, or another partition's method.
- [ ] Regression test: each reference kind refuses, and a self-contained moved method still yields a preview with zero new `compile_check` errors.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6; 2026-09-25 ctor-injection-precision review). Pre-existing on `main`: `BuildPartitionFile` (`SymbolRefactorService.cs:768`) emits only moved methods plus instance fields; `BuildForwardingMethod` (`:1254`) builds forwarding stubs. The defects are visible (non-compiling previews), not silent, hence Medium.

## Context

- Split child of `split-service-with-di-refuse-unsupported-method-shapes` (plan 20261001T034545Z; one regression shape each). Siblings share `SymbolRefactorService.cs` and `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`, so they are chained by `deps`. Each ships independently: a refusal or a correct forward, never a half-supported shape.
