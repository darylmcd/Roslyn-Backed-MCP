# promotion-tier-batches-post-refresh — Ship experimental to stable tier promotions in bounded batches

**row:** `promotion-tier-batches-post-refresh` · **pri:** `Medium` · **size:** `—` · **deps:** `promotion-scorecard-refresh-toplevel-run`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs`
- `.claude/skills/promote-tier/SKILL.md`
- `README.md` (surface-count gate)

## Acceptance

- [ ] Experimental to stable tier promotions ship in bounded batches via the `/promote-tier` skill, each batch justified by the refreshed scorecard.
- [ ] Each batch keeps `README.md`'s "N tools (X stable / Y experimental)" line in sync — the `ReadmeSurfaceCountTests` gate (PR #294) asserts it against `ServerSurfaceCatalog`.

## Evidence

Parent acceptance bullet 2, untouched and gated on a fresh scorecard. Clusters brainstorm BRAIN-007 (stable-surface promotion scorecard system).

## Context

Split from `promotion-tier-execution-batch` (2026-09-02). Correctly dep-blocked on `promotion-scorecard-refresh-toplevel-run` — promotions cannot be justified from a v1.38.1 scorecard against a v4.1.2 surface.

**Size deliberately `—`: not decomposable until the refreshed scorecard exists**, because the scorecard determines which tools qualify and therefore how many batches there are. Split into bounded per-batch children once the gate opens.

**HOTSPOT.** Promotions touch the `ServerSurfaceCatalog.*.cs` partials, an addenda-listed hotspot gated by the RMCP001/RMCP002 catalog-tracking analyzers — schedule at most one catalog-touching initiative per wave.
