# undo-disk-walk-bom-and-physical-path-guard-shape — Undo disk walk: detect BOM-only drift and make the physical-path guard failure shape consistent

**row:** `undo-disk-walk-bom-and-physical-path-guard-shape` · **pri:** `Low` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] A file whose only drift is a BOM/encoding change is restored by the undo disk walk (red-first test).
- [ ] EnsurePhysicalPath failure surfaces with one shape in the solution and file paths (check release-policy Rule 1 before changing a public result).

## Evidence

- Undo PR-A plan sketches (c) and (d).
