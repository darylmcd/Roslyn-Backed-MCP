# iedit-service-param-object — Introduce shared options object for IEditService's repeated bool parameters

**row:** `iedit-service-param-object` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Services/IEditService.cs:43-51`

## Acceptance

- [ ] ApplyTextEditsAsync and ApplyMultiFileTextEditsAsync accept one shared options parameter instead of 3 duplicated bool params each
- [ ] Existing callers/implementations updated and compile clean

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02b-core-service-contracts::DG2-cleanliness
## Amendment — 2026-08-13 (backlog-sweep 20260813T172325Z, PR #1230 code-quality review)

When this row's options-object rework happens, fold in the boundary-canonical write target.

The held PR #1230 added `string? canonicalWritePath = null` as a trailing optional parameter on `IEditService.ApplyTextEditsAsync`. As a defaulted optional on a Core interface it is a silent-failure shape: a caller that should pin a canonical path but omits the argument silently gets the historical unpinned behavior. Two production callers already do exactly that (`SuppressionService` at both its call sites), and any future write path will default the same way.

Filed here rather than as a sibling row because a sibling would collide as a second PR editing this same parameter list.

**Add to this row's acceptance:** the options object carries the boundary-canonical write target as an explicit member, so omission is a compile-time decision rather than a silent default.
