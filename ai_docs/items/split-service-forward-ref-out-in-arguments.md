# split-service-forward-ref-out-in-arguments — Forward ref/out/in arguments with their modifiers in split_service_with_di forwarding stubs

**row:** `split-service-forward-ref-out-in-arguments` · **pri:** `Medium` · **size:** `S` · **deps:** `split-service-refuse-unsupported-method-signatures`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:1254`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] Forwarding stubs pass `ref`, `out` and `in` arguments with the matching modifier keyword (`BuildForwardingMethod` emits `SyntaxFactory.Argument(IdentifierName(...))` without `RefKindKeyword` today).
- [ ] Regression test: a moved method with one `ref`, one `out` and one `in` parameter yields a preview with zero new `compile_check` errors.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6; 2026-09-25 ctor-injection-precision review). Pre-existing on `main`: `BuildPartitionFile` (`SymbolRefactorService.cs:768`) emits only moved methods plus instance fields; `BuildForwardingMethod` (`:1254`) builds forwarding stubs. The defects are visible (non-compiling previews), not silent, hence Medium.

## Context

- Split child of `split-service-with-di-refuse-unsupported-method-shapes` (plan 20261001T034545Z; one regression shape each). Siblings share `SymbolRefactorService.cs` and `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`, so they are chained by `deps`. Each ships independently: a refusal or a correct forward, never a half-supported shape.
