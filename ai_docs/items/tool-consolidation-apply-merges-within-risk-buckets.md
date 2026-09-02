# tool-consolidation-apply-merges-within-risk-buckets — Merge the apply tools strictly within risk buckets

**row:** `tool-consolidation-apply-merges-within-risk-buckets` · **pri:** `Medium` · **size:** `—` · **deps:** `tool-consolidation-adr-and-alias-machinery`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (representative; exact file set fixed when this row is decomposed)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`

## Acceptance

- [ ] Apply merges stay WITHIN risk buckets — formatting / text-edit / code-transform / file-lifecycle / project-file — taking 26 applies to about 9, NOT to 1.
- [ ] Per-tool-name allow/deny permissioning and honest destructive hints are preserved; no merge widens what a single permitted tool name can do.
- [ ] Net surface lands near 117 tools (about 10-13k tokens off `tools/list`).

## Evidence

The audit's risk-aligned variant gives up only 3-4 tools of reduction versus the max merge while keeping the safety surface intact — that trade is the whole point of this child being separate from the preview merges.

## Context

Split from `risk-aligned-tool-consolidation` (2026-09-02). Dep-blocked on the ADR + alias machinery child.

**This is the dangerous half.** Merging applies across risk buckets would let one permitted tool name perform a materially more destructive operation than the name it replaced, silently widening any client's allow-list. The bucket boundary is a safety property, not a taxonomy preference.

**Size deliberately `—`** for the same reason as the preview-merge sibling: the ADR fixes the group boundaries first.

**Catalog hotspot** — RMCP001/RMCP002-gated; at most one catalog-touching initiative per wave.
