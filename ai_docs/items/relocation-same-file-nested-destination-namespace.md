# relocation-same-file-nested-destination-namespace — Honor the full destination namespace in same-file relocation

**row:** `relocation-same-file-nested-destination-namespace` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:292-312`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Relocating Outer.Inner.Worker to Target in its current file yields Target.Worker. Preserve unrelated enclosing declarations, consumer bindings and preview/apply compilation.

## Evidence

- Source review at `34a87d9c`. The sole-member branch renames the innermost node in place while preserving its outer namespace, so the destination can become Outer.Target.
