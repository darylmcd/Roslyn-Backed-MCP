# risk-aligned-tool-consolidation — Consolidate 173 → ~117 tools within risk buckets

**row:** `risk-aligned-tool-consolidation` · **pri:** `Medium` · **size:** `L` · **deps:** `sdk-2x-upgrade`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/` (18 disjoint merge groups spanning most tool files — L umbrella; split per group at plan time)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` (catalog + alias machinery `ToolAliasDeprecation.ForSisterAlias`)
- Structural anchors already shipped: every `*_apply` takes only previewToken+workspaceId; `symbol_refactor_preview` ships the kind-discriminated pattern

## Acceptance

- [ ] Preview merges per the audit's 18-group inventory (all 49 previews are uniformly readOnly=true/destructive=false — no annotation-granularity loss)
- [ ] Apply merges stay WITHIN risk buckets — formatting / text-edit / code-transform / file-lifecycle / project-file (26 applies → ~9, NOT → 1) — preserving per-tool-name allow/deny permissioning and honest destructive hints
- [ ] Old names survive as declared deprecated aliases for ≥1 minor cycle; removal only at the next major; ADR + migration note (public surface)
- [ ] Net surface ≈117 tools ≈ ~10–13k tokens off tools/list; ToolSearch precision improves (kind names listed in merged descriptions)

## Evidence

- 18 disjoint groups, max merge 173→~113; risk-aligned variant gives up only 3–4 tools of reduction while keeping the safety surface — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §2

## Notes

- Ride the `sdk-2x-upgrade` major window; fold `apply-composite-preview-destructive-misnomer` (rename) into the same cycle.
- Dynamic toolsets rejected: list_changed unreliable across clients; GitHub removed theirs 2026-05-20 (report §6).
- Related-not-duplicate: `tool-surface-pagination-or-tool-sets` (catalog resources, no hiding).
