# apply-composite-preview-destructive-misnomer — `_preview`-suffixed tool actually applies

**row:** `apply-composite-preview-destructive-misnomer` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs:94`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`

## Acceptance

- [ ] Tool + catalog entry renamed (`apply_composite`), OR a description warning added that this `_preview`-suffixed tool actually applies — if the name stays, document loudly why
- [ ] Regression: catalog test asserts name + Destructive flag are consistent with the tool's semantics

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phases 6/9/14, server v2.3.1 (split from `docs-tool-naming-and-revert-scope`). Source: 2026-05-31 surface-test.

## Context

`apply_composite_preview` is a DESTRUCTIVE apply (`Destructive=true` at `OrchestrationTools.cs:94`) yet carries the `_preview` suffix shared by read-only preview tools — misleading. NB: published package → a tool-name change is a consumer-facing contract change (Directive #4 — ADR + migration note).
