# dependency-inversion-nested-concrete-symbol-resolution — Resolve nested concrete metadata identity

**row:** `dependency-inversion-nested-concrete-symbol-resolution` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:843-848`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Resolve the originally selected nested nongeneric concrete symbol after extraction using its complete metadata identity, including enclosing types.
- [ ] Preserve top-level and nested-namespace controls; do not silently skip consumer rewrites or bind another same-name type.
- [ ] Prove public dependency-inversion preview and applied/reloaded consumer bindings and compilation with a red-first nested-type regression.

## Evidence

- Main 0e0f90661035a38e570627b1508dac2ea2496708 GetMetadataName concatenates namespace plus leaf MetadataName and omits ContainingType; current-session semantic source inspection confirms the defect. Generic interface declaration support has a separate owner. No runtime reproduction claimed.
