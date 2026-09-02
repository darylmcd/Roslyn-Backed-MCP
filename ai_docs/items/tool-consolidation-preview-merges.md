# tool-consolidation-preview-merges — Merge the preview tools per the audit inventory

**row:** `tool-consolidation-preview-merges` · **pri:** `Medium` · **size:** `—` · **deps:** `tool-consolidation-adr-and-alias-machinery`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (representative; exact file set fixed when this row is decomposed)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`

## Acceptance

- [ ] Preview merges land per the audit's 18-group inventory. All 49 previews are uniformly `readOnly=true` / `destructive=false`, so no annotation granularity is lost.
- [ ] Old names survive as declared deprecated aliases; each merged description lists its kind names so ToolSearch precision improves rather than degrades.

## Evidence

The audit's 18 disjoint merge groups; previews are the half with no annotation-granularity risk, which is why they are separated from the apply merges.

## Context

Split from `risk-aligned-tool-consolidation` (2026-09-02). Dep-blocked on the ADR + alias machinery child.

**Size deliberately `—`: not decomposable until the ADR fixes the group boundaries.** The audit lists 18 disjoint groups; splitting into per-group children before the policy is ratified would produce ~18 blocked rows whose boundaries the ADR may move. Split into bounded per-group children once the ADR lands.

**Catalog hotspot** — `ServerSurfaceCatalog.*.cs` is RMCP001/RMCP002-gated; at most one catalog-touching initiative per wave.
