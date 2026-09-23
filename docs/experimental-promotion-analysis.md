# Experimental → stable promotion analysis

This document supports the post-release roadmap item **“promote experimental → stable”** (`docs/roadmap.md`, `docs/parity-gap-matrix.md`). It does **not** change the catalog by itself — promotions ship via `ServerSurfaceCatalog.cs`, `docs/product-contract.md`, semver bump, and `CHANGELOG.md` per `docs/release-policy.md`.

## Scoring dimensions (per tool)

| Dimension | Weight | Notes |
|-----------|--------|--------|
| **Read-only vs mutation** | High | Read-only tools are easier to stabilize; destructive tools need stronger preview/apply evidence. |
| **Integration test coverage** | High | Prefer evidence in `tests/RoslynMcp.Tests/` (see `docs/coverage-baseline.md`). |
| **Contract simplicity** | Medium | Narrow JSON DTOs with few optional branches reduce breaking-change risk. |
| **Operational usage** | Medium | Field feedback, repo-matrix audit rollups under `ai_docs/reports/`, and supporting raw deep-review runs under `ai_docs/audit-reports/`. |
| **Schema/description accuracy** | Medium | Tool description matches behavior (audit PASS on schema-vs-behavior). |

## Aggregate surface (reference)

This document tracks promotion history and criteria, not the live surface totals. For current tool/resource/prompt counts, use `server_info` or `roslyn://server/catalog`.

Operational evidence for promotion decisions should come from the latest deep-review rollup in `ai_docs/reports/`, backed by immutable raw audits in `ai_docs/audit-reports/`.

**Current state (server 4.2.1):** the tool surface is 113 stable / 62 experimental (see [README](../README.md#live-surface)). Most of the tools named in the historical tiers below have since been promoted; the Tier 1 sections after v1.9.0 and the "Next promotion pass" section reflect that. Verify any individual tool's tier against the live catalog before relying on this document.

## Tier 1 — promoted (v1.6.0)

The following were promoted to **stable** in v1.6.0 per `docs/release-policy.md` (catalog + contract + semver).

| Tool | Category | Notes |
|------|----------|--------|
| `compile_check` | validation | Fast compileability signal without `dotnet build`. |
| `list_analyzers` | analysis | Diagnostics workflows; read-only. |
| `find_consumers` | analysis | Consumer/impact analysis; read-only. |
| `get_cohesion_metrics` | analysis | LCOM4 cohesion metrics; read-only. |
| `find_shared_members` | analysis | Refactor planning; read-only. |
| `analyze_snippet` | analysis | Ephemeral analysis without workspace; read-only. |

## Tier 1 — promoted (v1.8.0)

The following were promoted to **stable** in v1.8.0 (catalog `2026.04`): read-only advanced-analysis tools exercised across the 2026-04-08 repo-matrix deep-review batch. `semantic_search` was **not** promoted (ongoing relevance / empty-result UX work in `ai_docs/backlog.md`).

| Tool | Category | Notes |
|------|----------|-------|
| `find_unused_symbols` | advanced-analysis | Dead-code signal; read-only. |
| `get_di_registrations` | advanced-analysis | DI wiring scan; read-only. |
| `get_complexity_metrics` | advanced-analysis | Hotspot metrics; read-only. |
| `find_reflection_usages` | advanced-analysis | Reflection audit; read-only. |
| `get_namespace_dependencies` | advanced-analysis | Namespace graph; read-only. |
| `get_nuget_dependencies` | advanced-analysis | Package inventory; read-only. |

## Tier 1 — promoted (v1.9.0)

The following were promoted to **stable** in v1.9.0: `semantic_search` (backlog UX work shipped in #110, #123), flow-analysis tools (read-only, expression-bodied member support tested), and `evaluate_csharp` (timeout/budget/abandoned-cap enforcement tested).

| Tool | Category | Notes |
|------|----------|-------|
| `semantic_search` | advanced-analysis | Verbose-query fallback (#110), exact-match implementing predicate (#123). |
| `analyze_data_flow` | advanced-analysis | Read-only flow analysis; expression-bodied member support. |
| `analyze_control_flow` | advanced-analysis | Read-only flow analysis; expression-bodied member support. |
| `evaluate_csharp` | scripting | Timeout budget, infinite-loop safety, abandoned-cap, cancellation — all integration-tested. |

## Tier 1 — promoted (v1.11.0)

`get_code_actions`, `preview_code_action`, and `apply_code_action` were promoted to **stable** in v1.11.0 after selection-range refactorings were verified working in v1.10.0.

## Tier 1 — promoted (v1.12.0)

Seven tools were promoted to **stable** in v1.12.0 on multi-repo deep-review audit evidence: `get_syntax_tree`, `workspace_changes`, `suggest_refactorings`, `get_operations`, `get_editorconfig_options`, `evaluate_msbuild_property`, `evaluate_msbuild_items`. `get_msbuild_properties`, `apply_text_edit`, and `set_editorconfig_option` are also stable in the current catalog (promoted in later releases; see `CHANGELOG.md`).

## Next promotion pass

Repopulate Tier 1 candidates after the next repo-matrix audit rollup or when operational evidence justifies additional stable promotions. The earlier "likely candidates" list (`get_operations`, `get_syntax_tree`, and the configuration/MSBuild helpers) has been fully promoted, so the next candidates must be chosen from the live catalog's remaining experimental tools (read-only analysis tools are the easiest to stabilize; mutation tools need stronger preview/apply evidence) — each gated by `docs/release-policy.md`.

## Tier 2 — needs stronger evidence before promotion

Rows for tools that are now stable are marked **promoted**; only the still-experimental entries remain open.

| Bucket | Examples | Blocker |
|--------|----------|---------|
| Direct file / project mutation | ~~`apply_text_edit`~~ (**promoted**), ~~`set_editorconfig_option`~~ (**promoted**), `apply_project_mutation` (experimental) | Requires documented preview/apply + negative tests for path safety. |
| Code actions | ~~`get_code_actions`, `preview_code_action`, `apply_code_action`~~ (**promoted in v1.11.0**) | None remaining. |
| Orchestration / cross-project | `migrate_package_preview`, `move_type_to_project_preview` (both still experimental) | Multi-step previews; need characterization tests per workflow. |

## Tier 3 — intentionally long-tail experimental

- **Prompts** — excluded from compatibility API per `docs/product-contract.md`.
- **Dead-code / scaffolding / orchestration apply** — remain experimental until operational evidence and test depth justify stable tier.

## Promotion checklist (when executing a promotion)

1. Confirm tests + coverage for the tool’s service path.
2. Update `ServerSurfaceCatalog` entry from `"experimental"` to `"stable"`.
3. Update `docs/product-contract.md` stable tool families list.
4. Minor semver bump; `CHANGELOG.md` **Added** under stable surface.
5. Run `./eng/verify-release.ps1` and `./eng/verify-ai-docs.ps1`.
6. Re-run catalog parity: `SurfaceCatalogTests`.

## Related

- `docs/release-policy.md` — versioning and compatibility
- `docs/coverage-baseline.md` — measured coverage
- `docs/parity-gap-implementation-plan.md` — release verification context
