# split-service-copy-namespace-scoped-usings — Copy namespace-scoped usings into split_service_with_di partition files

**row:** `split-service-copy-namespace-scoped-usings` · **pri:** `Medium` · **size:** `S` · **deps:** `split-service-forward-ref-out-in-arguments`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:417`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:768`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] Partition files carry the usings declared inside the source namespace block as well as the file-level usings (`context.Usings` currently captures file-level only, `:417`), or the split is refused with a specific reason.
- [ ] Regression test: a moved method that depends on a namespace-scoped using yields a preview with zero new `compile_check` errors.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6; 2026-09-25 ctor-injection-precision review). Pre-existing on `main`: `BuildPartitionFile` (`SymbolRefactorService.cs:768`) emits only moved methods plus instance fields; `BuildForwardingMethod` (`:1254`) builds forwarding stubs. The defects are visible (non-compiling previews), not silent, hence Medium.

## Context

- Split child of `split-service-with-di-refuse-unsupported-method-shapes` (plan 20261001T034545Z; one regression shape each). Siblings share `SymbolRefactorService.cs` and `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`, so they are chained by `deps`. Each ships independently: a refusal or a correct forward, never a half-supported shape.
