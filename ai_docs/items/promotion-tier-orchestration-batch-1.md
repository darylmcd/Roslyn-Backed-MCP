# promotion-tier-orchestration-batch-1 — Promote qualifying experimental tools in the Orchestration catalog partial (batch 1 of 2)

**row:** `promotion-tier-orchestration-batch-1` · **pri:** `Medium` · **size:** `M` · **deps:** `promotion-scorecard-refresh-toplevel-run`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CrossProjectRefactoringTools`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools`
- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`

## Acceptance

- [ ] Each tool in this batch that the REFRESHED scorecard qualifies is promoted experimental -> stable: the `[McpToolMetadata]` tier argument and the matching `ServerSurfaceCatalog.Orchestration.cs` `Tool(...)` row change together.
- [ ] A tool the refreshed scorecard does NOT qualify stays experimental and the row records why — this batch is a bounded promotion opportunity, not a mandate to promote all its candidates.
- [ ] The `README.md` surface-count line is updated so `ReadmeSurfaceCountTests` stays green, and the stable-only callable count at `README.md:186` is re-derived rather than hand-adjusted.

## Evidence

Candidate experimental tools in this batch (measured on `main` 2026-09-02):

- `CrossProjectRefactoringTools` — `move_type_to_project_preview`, `extract_interface_cross_project_preview`, `dependency_inversion_preview`
- `OrchestrationTools` — `migrate_package_preview`, `split_class_preview`, `extract_and_wire_interface_preview`, `apply_composite_preview`
- `ProjectMutationTools` — `add_central_package_version_preview`, `apply_project_mutation`

Batch candidates: **9**. Whole surface at split time: 174 tools = 113 stable + 61 experimental.

## Context

Split from `promotion-tier-batches-post-refresh` (2026-09-02), itself split from `promotion-tier-execution-batch`.

**Dep-blocked by design.** Which tools qualify is decided by the refreshed scorecard, so `promotion-scorecard-refresh-toplevel-run` must land first. Do not promote from the stale v1.38.1 snapshot.

**Batching rule:** one `ServerSurfaceCatalog.*.cs` partial and at most three `Tools/*.cs` files per batch. The partials are an addenda-listed **hotspot** — at most one catalog-touching initiative per wave, so these batches must not run concurrently with each other or any other catalog-touching row.

**Gate-forced companions (NOT anchored above, but they WILL be edited).** The tier/name string lives in BOTH the `[McpToolMetadata]` attribute on the `Tools/*.cs` method AND the `Tool(...)` row in the matching `ServerSurfaceCatalog.*.cs` partial; the **RMCP001/RMCP002** analyzers fail the build when they disagree (`analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:72,87`). `README.md`'s surface-count line (`README.md:239`) plus the stable-only callable count (`README.md:186`) are gated by `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs`. `README.md` is deliberately left OUT of `## Anchors` so this row's anchor-derived size stays honest; the plan stanza MUST still count it in `productionFilesTouched` and cite the **gate-forced-companion** Rule 3 exemption with REAL captured gate output — the exemption is not valid on this row's say-so.

Ship via the `/promote-tier` skill (`.claude/skills/promote-tier/SKILL.md`).
