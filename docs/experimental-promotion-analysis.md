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

| Tier | Tools | Resources | Prompts |
|------|-------|-----------|---------|
| Stable | 62 | 9 stable / 0 experimental | 0 stable / 16 experimental |
| Experimental | 61 | — | — |

Authoritative counts: `ServerSurfaceCatalog.GetSummary()` / `server_info` / `roslyn://server/catalog`.

Operational evidence for promotion decisions should come from the latest deep-review rollup in `ai_docs/reports/`, backed by immutable raw audits in `ai_docs/audit-reports/`.

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

## Next promotion pass

Repopulate Tier 1 candidates after the next repo-matrix audit rollup or when operational evidence justifies additional stable promotions. Likely candidates still experimental: `semantic_search` (after backlog clears), flow-analysis tools, scripting (`evaluate_csharp`), configuration/MSBuild helpers — each gated by `docs/release-policy.md`.

## Tier 2 — needs stronger evidence before promotion

| Bucket | Examples | Blocker |
|--------|----------|---------|
| Direct file / project mutation | `apply_text_edit`, `set_editorconfig_option`, `apply_project_mutation` | Requires documented preview/apply + negative tests for path safety. |
| Code actions | `get_code_actions`, `preview_code_action`, `apply_code_action` | Large Roslyn surface; schema stability and idempotency need explicit policy. |
| Orchestration / cross-project | `migrate_package_preview`, `move_type_to_project_preview` | Multi-step previews; need characterization tests per workflow. |

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
