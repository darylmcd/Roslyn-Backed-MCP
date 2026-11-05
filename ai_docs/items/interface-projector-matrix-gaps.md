# interface-projector-matrix-gaps — Close construct-matrix and namespace-collection gaps in the projector

**row:** `interface-projector-matrix-gaps` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:60-110` (`Project`, `RequiredNamespaces`), `RecordReferencedTypes`
- `tests/RoslynMcp.Tests/InterfaceMemberProjectorTests.cs`

## Acceptance

- [ ] Matrix rows added and passing (or the defect fixed): generic source type, a nested type owned by the source type itself, partial methods, multi-declarator event field (`event Action A, B;`), a qualified non-public constant default (`Type.Hidden`), `__arglist`, float/decimal/char/escaped-string/enum defaults, `ref readonly` parameter, and the dependency-inversion and extract-and-wire routes.
- [ ] Namespace collection derives from the projected declaration instead of a second per-member-kind switch (`RecordReferencedTypes`), and `RequiredNamespaces` cannot be read before every `Project` call (no order-dependent mutable list).
- [ ] `ContractParameters` no longer strips `this` (dead: extension methods are static and never projectable).

## Evidence

- Cold review of PR #1875 FOLLOW-UP findings: untested constructs above; `RecordReferencedTypes` duplicates `TypeNamespaceWalker`/`Strip` knowledge and is call-order dependent.

## Notes

PR 1995 (cross-project-interface-parameter-semantics, merged 2026-11-05) covered part of bullet 1: __arglist, float/decimal/char defaults, ref readonly, DI/EAW routes, private-const default. Still open: generic-source-type, nested-type, partial-method and multi-declarator-event items. Also open: nameof(<instance property or method>) left symbolic when the member is excluded by a member subset (CS0103), typeof(SourceType)/third-assembly types in kept attribute arguments (see interface-projector-attribute-scope-and-inheritance), and the four-route preview switch still copied in CrossProjectInterfaceBindingIntegrationTests.cs:327, CrossProjectReceivingNamespaceIntegrationTests.cs:92, InterfaceBaseTypeInsertionIntegrationTests.cs:123, InterfaceNullableContextProjectionIntegrationTests.cs:177, DetachedFixtureNativeContextIntegrationTests.cs:45.
