# cross-project-interface-signature-reference-closure — Resolve interface signature references

**row:** `cross-project-interface-signature-reference-closure` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:183`
- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:297`
- `src/RoslynMcp.Roslyn/Helpers/ProjectGraphHelpers.cs:21`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Extraction with signature types in an independent third project adds valid acyclic target-to-owner references by symbol identity, retaining aliases and reference metadata, and compiles through direct extraction, dependency inversion and extract-and-wire.

## Evidence

- At `0121d9ab`, RequiredNamespaces supplies imports while extraction adds only the source-to-target reference. An independent signature owner remains unresolved. Cold review rejected a harness-only first slice.

## Context

- First independently correct part of `cross-project-interface-signature-dependencies`. Preserve source-owned cycle behavior until the dependent refusal row ships. Never add a cyclic edge.
