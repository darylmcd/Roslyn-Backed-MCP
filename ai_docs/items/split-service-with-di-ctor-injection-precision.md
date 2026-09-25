# split-service-with-di-ctor-injection-precision — Generated constructors must reproduce the original constructor's injection contract

**row:** `split-service-with-di-ctor-injection-precision` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`

## Acceptance

- [ ] Partition constructors inject only fields the original constructor(s) assigned from parameters (same rule the facade uses via `CollectInjectableConstructorAssignments`); an uninitialized field the constructor never assigned (`private int _calls;`) no longer becomes a DI parameter that cannot be resolved.
- [ ] Each injected parameter takes the ORIGINAL constructor parameter's type, not the field's type (`ILogger _log` assigned from `ILogger<Svc> log` must stay `ILogger<Svc>`).
- [ ] `x ?? throw ...` null guards accepted as parameter copies are reproduced in the generated constructors, not dropped.
- [ ] Multi-declarator fields inject only declarators the constructor assigned.
- [ ] More than one instance-constructor overload is refused with a specific reason (or the overload set is preserved), instead of collapsing into one generated facade constructor.
- [ ] Regression cases in `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs` cover each shape.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, findings 4, 5, 12, 14 + cycle-2 LOWs). Pre-existing in partition generation on `main`.
- `BuildPartitionConstructor` builds parameters from `field.Declaration.Type` and plumbs every uninitialized declarator of a referenced field (`ctorEligibleFields` filters only on initializers). Output compiles but DI activation fails at runtime (`Unable to resolve service for type System.Int32`, or unregistered non-generic `ILogger`).
- `BuildFacadeRoot` drops every instance constructor and emits one facade constructor, so overloads collapse.

## Context

Split out of `split-service-with-di-facade-drops-sibling-types` on 2026-09-25 so its closure does not drop recorded findings. Raised Low → High after the cold review showed the partition side yields runtime DI activation failures on common inputs (`ILogger<T>`).
