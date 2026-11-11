# extract-type-catalog-description-duplicated — Derive the surface catalog from tool attributes

**row:** `extract-type-catalog-description-duplicated` · **pri:** `Low` · **size:** `M` · **verified:** `95f651345b37a2849fe02bd026f827813658573f`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs:18-23` — McpToolMetadata summary and `[Description]` attribute (different text by design).
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs:34` — catalog entry text equal to the McpToolMetadata summary.
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs:402-450` — drift gate (Summary equality at :444).

## Acceptance

- [ ] The catalog entry text is derived from the tool attributes for all tools (not just extract_type_preview), so one edit updates one place; SurfaceCatalogTests drift gate adapted, not weakened.
- [ ] Any resulting text change is reviewed as wire-visible metadata (the catalog is served via `roslyn://server/catalog`).

## Evidence

- 2026-11-11 re-derivation: THREE texts exist, not two: McpToolMetadata summary == catalog entry (drift-gated), and the `[Description]` attribute differs by design. The earlier 200-char budget claim is false: the only budget test (`DietedToolMethodDescriptions_StayWithinSliceCeiling`, SurfaceCatalogTests.cs:482) covers only SymbolTools, AdvancedAnalysisTools and AnalysisTools at 250 chars.
- Single-tool dedupe is not a correct fix: it would leave every other tool duplicated. Operator decision needed on wire-visible description changes before the all-tools derivation.
