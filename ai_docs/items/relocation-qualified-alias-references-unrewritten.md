# relocation-qualified-alias-references-unrewritten — Relocation never rewrites qualified or alias-target references to the moved type

**row:** `relocation-qualified-alias-references-unrewritten` · **pri:** `Medium` · **size:** `M` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] After relocation, `Old.Selected` expressions and `using W = Old.Selected;` aliases bind to the new namespace and the solution compiles.
- [ ] Red-first tests for a qualified type reference and an alias target.

## Evidence

- reloc-b report sketch 3 (2026-11-18): the import pass only adds/removes plain usings; syntactically qualified references and alias targets are left pointing at the old namespace.
