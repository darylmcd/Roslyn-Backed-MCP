# tool-consolidation-deprecated-alias-registry — Add a catalog-owned deprecated-alias registry

**row:** `tool-consolidation-deprecated-alias-registry` · **pri:** `Low` · **size:** `M` · **deps:** `tool-consolidation-policy-foundation`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolAliasDeprecation.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`
- `tests/RoslynMcp.Tests/AliasToolsTests.cs`

## Acceptance

- [ ] Make one catalog-owned declaration record alias name, canonical name, reason, risk bucket, introduction release, and earliest removal major.
- [ ] Relocate the existing response-envelope type rather than create a second alias model.
- [ ] Register `get_symbol_outline` → `document_symbols` as the bounded representative proof.
- [ ] Keep the old name registered and callable; expose deprecation metadata in the catalog.
- [ ] Prove alias/canonical category, tier, safety annotations, parameters, and response payload remain equal apart from deprecation.

## Evidence

`ToolAliasDeprecation` currently lives under `Tools/` and only shapes response notices. `ServerSurfaceCatalog` has no declared alias/deprecation lifecycle.

## Context

Depends on `tool-consolidation-policy-foundation`. Unblocking child split from `tool-consolidation-adr-and-alias-machinery`.
