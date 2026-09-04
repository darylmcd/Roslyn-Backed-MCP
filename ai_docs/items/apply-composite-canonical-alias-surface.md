# apply-composite-canonical-alias-surface — Add the canonical composite-apply name

**row:** `apply-composite-canonical-alias-surface` · **pri:** `Low` · **size:** `M` · **deps:** `tool-consolidation-deprecated-alias-registry`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`
- `README.md`
- `src/RoslynMcp.Host.Stdio/README.md`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`
- `tests/RoslynMcp.Tests/CatalogDestructiveWarningTests.cs`

## Acceptance

- [ ] Add canonical `apply_composite`; retain `apply_composite_preview` as a declared deprecated alias delegating to the same core.
- [ ] Keep both entries destructive and parameter-compatible; make the old description name the replacement.
- [ ] Update both count-gated READMEs for the temporary extra alias and add the public migration fragment.
- [ ] Prove by reflection/catalog tests that both names resolve to one safety-equivalent surface.

## Evidence

The destructive operation is currently exposed only under the misleading `apply_composite_preview` name.

## Context

Depends on `tool-consolidation-deprecated-alias-registry`. Absorbs `apply-composite-preview-destructive-misnomer`.
