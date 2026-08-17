# tool-surface-pagination-or-tool-sets — tool-set catalog resources for bounded discovery

**row:** `tool-surface-pagination-or-tool-sets` · **pri:** `Low` · **size:** `M` · **deps:** `resource-read-protocol-error-semantics` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`

## Acceptance

- [ ] Catalog summary advertises the tool-set resource; each named set returns only matching categories with offset/limit/hasMore metadata
- [ ] Unknown set returns structured `InvalidArgument`; existing full and paginated catalog resources remain unchanged
- [ ] No change to MCP tool registration or default `tools/list` visibility unless the implementation note proves the client supports that safely

## Evidence

- Surface count drift across recent versions (`server_info.surface.registered.tools` 151 → 173 since v1.27); MCP spec tools/list pagination; 2026-05-20 no-context subagent probe showed correctly steered agents chose Roslyn primitives without needing a full catalog dump. Source: 2026-05-05 MCP-best-practices comparison §3 rec J plus 2026-05-20 agent-view review.

## Context

173 tools is approaching small-model discovery saturation, but current evidence points to a routing/steering problem more than a raw-count problem. Now that `recommend_workflow` exists, wait for fresh post-router evidence before adding tool-set catalog resources (for example `roslyn://server/catalog/tool-sets` and `roslyn://server/catalog/tools/{toolSet}/{offset}/{limit}`) that expose bounded subsets such as `navigation`, `refactoring`, `validation`, and `analysis` without hiding tools from clients that can handle the full surface.

**Weaker evidence — N until small-model discovery friction is reported after the router lands externally.**
