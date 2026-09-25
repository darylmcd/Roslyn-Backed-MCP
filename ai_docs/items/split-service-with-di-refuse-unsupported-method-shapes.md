# split-service-with-di-refuse-unsupported-method-shapes — Refuse moved-method shapes the partition cannot compile

**row:** `split-service-with-di-refuse-unsupported-method-shapes` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`

## Acceptance

- [ ] `split_service_with_di_preview` refuses (specific reason) when a moved method references a kept method, property, static member, base member, or another partition's method.
- [ ] Refuses (or correctly forwards) moved `static`, `override`/`abstract`, explicit-interface, `ref`-returning, and generic-with-constraints methods, and generic source types.
- [ ] Forwarding stubs pass `ref`/`out`/`in` arguments with their modifiers.
- [ ] Regression test: each shape either refuses or yields a preview with zero new `compile_check` errors.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6). Pre-existing on `main`: partition files carry only the moved methods + instance fields (`BuildPartitionFile`); `BuildForwardingMethod` emits `SyntaxFactory.Argument(IdentifierName(...))` without `RefKindKeyword`, drops `ExplicitInterfaceSpecifier` and constraint clauses; a static stub references the instance partition field.
- Visible (non-compiling) rather than silent, hence Medium.

## Context

Filed 2026-09-25 while shipping the sibling-types fix.
