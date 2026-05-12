# Design note: `/promote-tier` prompt and resource promotion paths

**Status:** decision taken — see Recommendation section.
**Date:** 2026-05-12
**Author:** backlog-sweep executor (initiative `promote-tier-supports-prompts-and-resources`)

---

## Background

The `/promote-tier` skill (`.claude/skills/promote-tier/SKILL.md`) already wires three resolver paths in Step 2 — one per surface kind. The tools path (Step 2 / "Tools") performs a two-site atomic flip: it edits the `[McpToolMetadata]` attribute on the implementing method AND the matching row in the `ServerSurfaceCatalog` partial. The `SurfaceCatalogTests.McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` test enforces that dual-write contract at CI time.

The resources and prompts paths (Step 2 / "Resources" and "Prompts") already exist in SKILL.md at lines 106–113, but they are intentionally catalog-only today: neither `[McpServerResource]` nor `[McpServerPrompt]` carries a tier-bearing metadata attribute analogous to `[McpToolMetadata]`. The tier for resources and prompts lives exclusively in the `ServerSurfaceCatalog.Resources.cs` and `ServerSurfaceCatalog.Prompts.cs` partials respectively, and `/promote-tier` already reads and edits those partials when the resolved kind is `resource` or `prompt`.

This design note settles whether that catalog-only treatment is sufficient as a permanent contract, or whether parity with the tools pattern requires introducing `[McpServerPromptMetadata]` and `[McpServerResourceMetadata]` attributes.

---

## Current-state audit

### Prompt surface (20 entries)

`ServerSurfaceCatalog.Prompts.cs` defines 20 `Prompt(...)` factory entries, all currently `"experimental"`. The implementing methods are spread across four partial files:

| File | Prompts |
|---|---|
| `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs` | `explain_error`, `suggest_refactoring`, `review_file`, `analyze_dependencies` (4) |
| `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs` | `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `refactor_loop` (6) |
| `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.GuidedWorkflows.cs` | `guided_extract_method`, `msbuild_inspection`, `session_undo` (3) |
| `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs` | `security_review`, `discover_capabilities`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact` (7) |

No method carries a tier annotation today — only `[McpServerPrompt(Name = "...")]` and `[Description("...")]`.

### Resource surface (13 entries)

`ServerSurfaceCatalog.Resources.cs` defines 13 `Resource(...)` factory entries. Tier assignments are mixed (`stable` / `experimental`). The implementing methods live in two files:

| File | Resources |
|---|---|
| `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs` | `server_catalog`, `server_catalog_full`, `server_catalog_tools_page`, `server_catalog_prompts_page`, `resource_templates` (5) |
| `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs` | `workspaces`, `workspaces_verbose`, `workspace_status`, `workspace_status_verbose`, `workspace_projects`, `workspace_diagnostics`, `source_file`, `source_file_lines` (8) |

No method carries a tier annotation today — only `[McpServerResource(UriTemplate=..., Name=...)]` and `[Description("...")]`.

### Existing attribute

`src/RoslynMcp.Host.Stdio/Catalog/McpToolMetadataAttribute.cs` defines `McpToolMetadataAttribute` with constructor shape `(category, supportTier, readOnly, destructive, summary, outputSchemaTypeRef?)`. No equivalent attribute exists for resources or prompts.

### Existing parity test

`tests/RoslynMcp.Tests/SurfaceCatalogTests.cs` contains:

- `ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts` — name-level parity: every `[McpServerTool]` / `[McpServerResource]` / `[McpServerPrompt]` attribute must appear in the corresponding catalog list.
- `McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` — dual-write parity: every `[McpServerTool]` method must carry `[McpToolMetadata]` and its fields must match the catalog entry on all five structural fields.

There is no equivalent of the second test for resources or prompts today.

---

## Option A — catalog-only promotion (no new attribute types)

### What it means

The tier for prompts and resources is authoritative only in the catalog partials (`ServerSurfaceCatalog.Prompts.cs` and `ServerSurfaceCatalog.Resources.cs`). `/promote-tier` already supports this: Step 2 routes `kind == resource` and `kind == prompt` to catalog-only edits (Step 3 is skipped). Step 4 edits the catalog partial. Step 5 runs `SurfaceCatalogTests`, which passes because the name-level coverage test is satisfied and there is no dual-write parity test for resources/prompts.

### Files affected

None — this is the status quo. The skill and catalog already work correctly for this option.

### Tradeoffs

**Pros:**
- Zero implementation cost; `/promote-tier` works correctly for resources and prompts today.
- Catalog partials are the single source of truth — no dual-write, no drift surface.
- New resources/prompts only require a catalog row; the author cannot forget an attribute because there is no attribute to forget.
- `SurfaceCatalogTests.ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts` still catches name-level omissions (missing catalog rows).

**Cons:**
- No compile-time or test-time guard against catalog field drift on resources/prompts. A careless edit to the catalog partial (e.g. wrong `readOnly` flag or wrong `category`) will not be caught until a human reads the catalog.
- Asymmetric pattern: tools enjoy dual-write enforcement while resources/prompts rely on catalog discipline alone. The asymmetry is documented (in SKILL.md Step 2) but may surprise future contributors.
- If a resource/prompt's `readOnly`, `destructive`, `summary`, or `category` field drifts from reality, there is no automated safety net.

---

## Option B — introduce `[McpServerPromptMetadata]` and `[McpServerResourceMetadata]`

### What it means

Two new attribute types are introduced, mirroring `McpToolMetadataAttribute`:

- `McpServerPromptMetadataAttribute`: constructor `(category, supportTier, readOnly, destructive, summary)` — same shape as `McpToolMetadataAttribute` minus `outputSchemaTypeRef` (prompts do not emit structured output).
- `McpServerResourceMetadataAttribute`: same constructor shape. Resources also do not currently use `outputSchemaTypeRef`.

Every prompt method gains `[McpServerPromptMetadata("prompts", "experimental", true, false, "...")]` inline. Every resource method gains `[McpServerResourceMetadata("server|workspace|analysis", "stable|experimental", true, false, "...")]` inline. `SurfaceCatalogTests` gains two new parity-check test methods mirroring `McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry`.

`/promote-tier` SKILL.md Steps 2 and 3 are updated: the "Resources" and "Prompts" resolver paths become dual-site (attribute + catalog) instead of catalog-only.

### File-count estimate

Production files:
- New: `src/RoslynMcp.Host.Stdio/Catalog/McpServerPromptMetadataAttribute.cs` (1)
- New: `src/RoslynMcp.Host.Stdio/Catalog/McpServerResourceMetadataAttribute.cs` (1)
- Annotated: `RoslynPrompts.cs`, `RoslynPrompts.RefactoringWorkflows.cs`, `RoslynPrompts.GuidedWorkflows.cs`, `RoslynPrompts.AnalysisWorkflows.cs` (4) — 20 method annotations
- Annotated: `ServerResources.cs`, `WorkspaceResources.cs` (2) — 13 method annotations

Total production files changed: 8

Test files:
- Extended: `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs` (1) — 2 new test methods (`McpServerPromptMetadata_RequiredOnEveryPrompt_MatchesCatalogEntry`, `McpServerResourceMetadata_RequiredOnEveryResource_MatchesCatalogEntry`)

Skill files:
- Updated: `.claude/skills/promote-tier/SKILL.md` (1) — Steps 2 and 3 "Resources" and "Prompts" resolver paths become dual-site

Total files changed or added: 10 (8 production + 1 test + 1 skill)

### Tradeoffs

**Pros:**
- Uniform enforcement across all three surface kinds; the asymmetry between tools and resources/prompts is eliminated.
- CI catches catalog field drift on all three surfaces.
- `/promote-tier` Step 5 (`SurfaceCatalogTests`) becomes a comprehensive parity gate for the entire surface.
- Consistent authoring pattern: a contributor adding a new prompt or resource is nudged to annotate it or CI fails.

**Cons:**
- Annotation surface is non-trivial: 20 prompt methods + 13 resource methods = 33 annotation sites, across 6 implementation files.
- The attribute shape for prompts and resources currently has no unique fields vs. tools (no `outputSchemaTypeRef` analog is planned). Option B introduces two attribute types that are structurally identical to `McpToolMetadataAttribute` minus one optional field — the only differentiator is `AttributeTargets.Method` applied to prompt/resource methods rather than tool methods. This is a modest type system cost.
- `readOnly` and `destructive` semantics are well-defined for tools (they describe mutation intent). For resources, `readOnly` is always `true` (resources are read-only by MCP contract) and `destructive` is always `false`. Encoding that in 13 attribute annotations is redundant — a future simplification might collapse those fields to compile-time constants for resource attributes.

---

## Recommendation: Option A (catalog-only) — permanent, not provisional

**Rationale:**

The asymmetry between tools and resources/prompts is justified, not accidental. The three surface kinds have fundamentally different mutation semantics:

- **Tools** can be destructive, can have side effects, and require per-tool discipline on `readOnly`/`destructive` flags that an author could plausibly set incorrectly. The `[McpToolMetadata]` dual-write contract prevents silent flag drift on a surface where incorrect flags have real operational consequences (`CatalogDestructiveWarningTests` tests the warning pipeline that fires on `readOnly=false, destructive=true` combinations).
- **Prompts** are always read-only and non-destructive by MCP contract. A `readOnly=false, destructive=true` prompt annotation would be nonsensical. There is no destructive-warning pipeline for prompts, so the drift risk for those two fields is zero.
- **Resources** are structurally read-only by MCP contract; the `readOnly=true, destructive=false` fields in every resource catalog row are fixed invariants, not per-resource decisions. Only `category`, `supportTier`, and `summary` carry meaningful per-resource information.

In practice, the only field that matters for `/promote-tier` on resources and prompts is `supportTier`. Category and summary are authoring-time decisions made once when the catalog row is created and almost never change after that. The tier promotion workflow — the one operation that requires the dual-write safety net for tools — has no ambiguity for resources/prompts: there is exactly one site (the catalog partial) and `/promote-tier` already handles it correctly.

The catalog-only pattern also has a composition advantage: new resources and prompts require exactly one artifact (the catalog row). The dual-write pattern for tools requires two artifacts (attribute + catalog row) because the attribute carries information the MCP framework needs at runtime (specifically, `outputSchemaTypeRef` for structured-content routing). No such runtime need exists for resource or prompt metadata today.

**Consequence for `/promote-tier`:** no changes needed. SKILL.md Step 2 already documents the catalog-only paths for resources and prompts and includes a forward-looking note instructing future maintainers to update the skill if a tier marker is ever added to the resource/prompt attribute. That note remains the correct contract.

---

## Follow-on implementation

This initiative is documentation-only — it settles the design decision. No follow-on implementation row is opened as a result of choosing Option A.

If a future contributor revisits Option B (for example, if a new `outputSchemaTypeRef`-equivalent need emerges for prompts, or if drift incidents on resource category/summary fields motivate tighter enforcement), the follow-on row `do` cell should read:

> Introduce `McpServerPromptMetadataAttribute` and `McpServerResourceMetadataAttribute` in `src/RoslynMcp.Host.Stdio/Catalog/`, annotate all 20 prompt methods across the 4 `RoslynPrompts.*.cs` partial files and all 13 resource methods across `ServerResources.cs` and `WorkspaceResources.cs`, extend `SurfaceCatalogTests` with two new parity-check test methods, and update `.claude/skills/promote-tier/SKILL.md` Steps 2 and 3 to perform dual-site edits for resources and prompts. File count: 10 files changed or added (8 production, 1 test, 1 skill). Reference: `ai_docs/items/promote-tier-prompts-and-resources-design.md` Option B analysis.

No such row is opened now because Option A is the settled path.
