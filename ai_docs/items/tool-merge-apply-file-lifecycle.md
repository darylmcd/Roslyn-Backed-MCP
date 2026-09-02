# tool-merge-apply-file-lifecycle — Merge 5 apply tools into `file_lifecycle_apply`

**row:** `tool-merge-apply-file-lifecycle` · **pri:** `Medium` · **size:** `M` · **deps:** `tool-consolidation-adr-and-alias-machinery`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Editing.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`

## Acceptance

- [ ] `create_file_apply`, `delete_file_apply`, `move_file_apply`, `scaffold_test_apply`, `scaffold_type_apply` are served by one kind-discriminated `file_lifecycle_apply`, mirroring the pattern `symbol_refactor_preview` already ships.
- [ ] Every old name survives as a declared deprecated alias using the machinery landed by `tool-consolidation-adr-and-alias-machinery`; no name is removed in this row.
- [ ] The merge stays strictly WITHIN one risk bucket, so per-tool-name allow/deny permissioning is preserved and no merged name can perform a more destructive operation than any name it replaces.
- [ ] The merged description lists the kind names so ToolSearch precision improves rather than degrades, and the catalog test asserts alias parity.

## Evidence

Members and their declaring files (measured on `main` 2026-09-02):

- `create_file_apply` — `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs` / `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Editing.cs`
- `delete_file_apply` — `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs` / `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Editing.cs`
- `move_file_apply` — `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs` / `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Editing.cs`
- `scaffold_test_apply` — `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs` / `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`
- `scaffold_type_apply` — `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs` / `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs`

Anchored production files: 2 tool file(s) + 2 catalog partial(s) = 4. `README.md` is a gate-forced companion on top (see Context).

## Context

Split from `tool-consolidation-apply-merges-within-risk-buckets` (2026-09-02).

**Dep-blocked on `tool-consolidation-adr-and-alias-machinery`** — a published-surface contract change (Directive #4), so the deprecation policy and alias mechanism must exist and be proven before any merge ships.

**Group boundaries are DERIVED, not ratified.** The 2026-08-13 audit reports "18 disjoint consolidation groups" but never enumerates them; this group was derived from the live surface by semantic family and risk bucket. **The ADR child may move these boundaries — re-check this member list against the ADR before implementing.**

Risk bucket: **file-lifecycle** (5 applies), spanning the Editing and Orchestration catalog partials.

**Gate-forced companions (NOT anchored above, but they WILL be edited).** The tier/name string lives in BOTH the `[McpToolMetadata]` attribute on the `Tools/*.cs` method AND the `Tool(...)` row in the matching `ServerSurfaceCatalog.*.cs` partial; the **RMCP001/RMCP002** analyzers fail the build when they disagree (`analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:72,87`). `README.md`'s surface-count line (`README.md:239`) plus the stable-only callable count (`README.md:186`) are gated by `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs`. `README.md` is deliberately left OUT of `## Anchors` so this row's anchor-derived size stays honest; the plan stanza MUST still count it in `productionFilesTouched` and cite the **gate-forced-companion** Rule 3 exemption with REAL captured gate output — the exemption is not valid on this row's say-so.
