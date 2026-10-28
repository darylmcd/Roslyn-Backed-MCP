# interface-projector-attribute-scope-and-inheritance — Qualify and inherit contract attributes in the interface projector

**row:** `interface-projector-attribute-scope-and-inheritance` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:244` (`ContractAttributes`), `:130` (`Strip`)
- `tests/RoslynMcp.Tests/InterfaceMemberProjectorTests.cs`

## Acceptance

- [ ] An allowlisted attribute whose argument names a type-scoped constant or nested type (`[DefaultParameterValue(Max)]`) is qualified or inlined through the same `ScopeQualifier` as defaults, and its types reach `RequiredNamespaces`.
- [ ] Contract attributes inherited by an override from its base method (`[NotNullWhen]`, caller-info) are copied from the symbol's `GetAttributes()` chain, not only the override's own syntax.
- [ ] Matrix rows fail on current main (CS0103/missing attribute) and pass after.

## Evidence

- Cold review of PR #1875: `ContractAttributes` is not routed through `ScopeQualifier`; `RecordReferencedTypes` records only the attribute class.
