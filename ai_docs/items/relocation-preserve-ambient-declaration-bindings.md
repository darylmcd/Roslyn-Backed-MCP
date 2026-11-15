# relocation-preserve-ambient-declaration-bindings — Retain ambient declaration bindings across namespace relocation

**row:** `relocation-preserve-ambient-declaration-bindings` · **pri:** `Medium` · **size:** `M` · **verified:** `9e7a94395a5cbc09a79b0d4c6b8387ce65809d39`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:287`
- `src/RoslynMcp.Roslyn/Helpers/MovedDeclarationFileWriter.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Prove red first with an ambient type from the old namespace and a same-name destination decoy. Preserve the actual bound symbol after preview, apply and reload for both destination-file shapes. Keep unchanged siblings and their bindings.

## Evidence

- At 9e7a9439 the relocation paths rename or copy the original syntax without rebinding unqualified references that depended on enclosing namespaces. Import synthesis only repairs actual directives.
