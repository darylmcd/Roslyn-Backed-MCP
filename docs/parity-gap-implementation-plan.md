# Parity gap matrix — implementation plan

<!-- Human-facing status and next steps for items in docs/parity-gap-matrix.md. -->

This document walks **`docs/parity-gap-matrix.md`** and records **what is already implemented**, **what to verify before a release**, and **what remains product roadmap** (not a bug).

## How to use this file

| Column | Meaning |
|--------|---------|
| **Matrix item** | Bullet from the parity matrix or roadmap section. |
| **Status** | `Done` = implemented in repo; `Verify` = needs release check; `Roadmap` = deferred by design; `Boundary` = documented non-goal. |
| **Evidence / next steps** | Where to look or what to do next. |

---

## Must-Have Before Release

| Matrix item | Status | Evidence / next steps |
|-------------|--------|------------------------|
| `server_catalog` | Done | Resource `roslyn://server/catalog` (`ServerResources.GetServerCatalog`), `ServerSurfaceCatalog` + `CatalogVersion`. **Release check 2026-04-04:** `SurfaceCatalogTests.ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts` passes; catalog aligns with registered MCP surface. Family-level contract in `docs/product-contract.md` matches tier intent (stable vs experimental). |
| `server_info` + tiers | Done | `ServerTools`, `ServerSurfaceCatalog` stable/experimental labels. **Release check 2026-04-04:** `SurfaceCatalogTests.ServerInfo_IncludesSurfaceSupportSummary` passes; `eng/verify-release.ps1 -Configuration Release` green (209 tests; v1.6.0). |
| Explicit stable vs experimental surface | Done | `docs/product-contract.md`, `ServerSurfaceCatalog`, catalog JSON. **Release check 2026-04-04:** no drift; promotion remains gated by `docs/release-policy.md` + release notes. |
| Wrapper/integration tests for under-tested tool families | Done | `WorkspaceToolsIntegrationTests`, `RefactoringToolsIntegrationTests`, `ValidationToolsIntegrationTests`, `WorkspaceResourceTests`, `PromptSmokeTests`; `SurfaceCatalogTests` for catalog parity. **Release check 2026-04-04:** full `dotnet test` via `verify-release` — 209 passed (includes `HighValueCoverageIntegrationTests`, `BoundedStoreEvictionTests`, `ServiceCollectionExtensionsTests`). |
| Workspace/session limits and failed-load cleanup | Done | `WorkspaceManagerOptions` (defaults 8 / 500), `WorkspaceManager`, `WorkspaceExecutionGate`. **Release check 2026-04-04:** env defaults in `ai_docs/runtime.md` verified against `Program.cs` + `WorkspaceManagerOptions` / `ValidationServiceOptions` / `PreviewStoreOptions` / `ExecutionGateOptions` / `SecurityOptions`. |
| Bounded related-test scans and command timeouts | Done | `ValidationServiceOptions` (5 min build / 10 min test / 25 related files default), `WorkspaceExecutionGate`, `TestRunnerService`, `TestDiscoveryService`. **Release check 2026-04-04:** defaults documented in `ai_docs/runtime.md` match code. |
| Canonical CI and publish verification path | Done | `.github/workflows/ci.yml`, `eng/verify-release.ps1`, `eng/verify-ai-docs.ps1` (see `CI_POLICY.md`). **Release check 2026-04-04:** local `verify-release` produced `artifacts/publish/host-stdio` and `artifacts/manifests/host-stdio-sha256.txt`; CI uploads `host-stdio-publish` and `release-manifests` on green runs. |
| Documented compatibility, deprecation, release policy | Done | `docs/release-policy.md`, `README.md`, `AGENTS.md`. **Release check 2026-04-04:** policy unchanged; `CHANGELOG.md` [1.5.0] documents current release line. |

### Release verification log (automatable evidence)

| When | Commit | Check |
|------|--------|--------|
| 2026-04-04 | `1f2e4fb8382666da0bf7f15b456ff2f72931eed3` | `./eng/verify-release.ps1 -Configuration Release` — build OK, **196** tests passed, publish + SHA-256 manifest written. |
| 2026-04-04 | `f61adb629ef564edf4dc36b82b68ce285d52c56d` | `./eng/verify-release.ps1 -Configuration Release` — build OK, **209** tests passed, v1.6.0 stable promotions + coverage uplift; publish + SHA-256 manifest written. |
| 2026-04-11 | (v1.9.0 promotion) | `just ci` — build OK, **329** tests passed, v1.9.0 stable promotions (`semantic_search`, `analyze_data_flow`, `analyze_control_flow`, `evaluate_csharp`); 66 stable / 57 experimental tools. |

---

## Comparison summary (matrix table)

| Area | Status | Notes |
|------|--------|--------|
| Standalone Roslyn MCP (navigation, refactor, build/test) | Done / ongoing | Ship posture: keep contract + tests aligned. |
| VS-backed servers (unsaved buffers, IDE fidelity) | Boundary | **Roadmap:** separate host/editor integration — see `docs/roadmap.md`. |
| MCP production best practices (discoverability, tiers, limits) | Done / Verify | Local-first; remote host deferred. |
| AI agent needs (stable paths, bounded mutation) | Done / ongoing | Enforce preview/apply in `ai_docs/runtime.md` policy. |

---

## Post-Release Roadmap (not current gaps)

| Item | Status | Notes |
|------|--------|--------|
| Second host HTTP/SSE + auth, policy, observability | Roadmap | `docs/roadmap.md` — new project when requirements are staffed. |
| Editor-backed or VS-backed live workspace | Roadmap | Same — not `MSBuildWorkspace`-only. |
| Large-solution indexing / cache | Roadmap | **See recommendations below.** |
| Promote experimental → stable | Roadmap | After operational evidence; use `docs/release-policy.md`. |

---

## Out of scope for this release (boundaries)

| Item | Status | Notes |
|------|--------|--------|
| Parity with live IDE while on `MSBuildWorkspace` | Boundary | Documented in product contract and matrix. |
| Prompts as compatibility API | Boundary | Prompts are not stable. |
| Remote deployment guidance without remote host | Boundary | Point to local stdio + Docker in `docs/setup.md` only. |

---

## Known architecture limitation (not a matrix “must-have”)

| ID | Topic | Status | Notes |
|----|--------|--------|--------|
| AUDIT-21 | IDE/CA analyzers vs SDK-implicit diagnostics in `MSBuildWorkspace` | Documented gap | `ai_docs/architecture.md` — fix only if product requires full analyzer load; likely separate spike. |

---

## Recommendations: hardening/quality vs large-solution performance

**If you must pick one track next:**

| Track | Choose when… | Primary work |
|-------|----------------|-------------|
| **Hardening / quality** | You want a **confident release** or fewer production surprises: regressions, edge cases, contract drift, and test coverage for high-risk tools. | Expand tests, fuzz boundaries, tighten validation, release checklist automation, dependency/CodeQL hygiene. |
| **Large-solution performance** | You have **real repos** where load time, memory, or operation latency blocks adoption; you can measure before/after. | Profiling, optional indexing/caching, workspace warmup, tighter execution gates — aligns with `docs/roadmap.md` “Large-Solution Performance Strategy”. |

**Recommendation:** Prefer **hardening/quality first** unless you already have profiling data showing a specific large-solution bottleneck. Performance work is easier to justify with metrics; hardening reduces release risk before you add more moving parts (caches, background indexing).

**Optional:** Run a **short profiling session** on your largest representative solution. If P95 operations are already acceptable, stay on **hardening** until metrics say otherwise.

---

## Doc maintenance

- **doc-audit** runs during implementation phases: after each meaningful change, refresh `ai_docs/README.md`, `ai_docs/runtime.md` (commands/env), and this file when parity status shifts.
- When this plan is **fully satisfied** for a release, add a short note under **Findings** in `.ai-doc-audit.md` or update the **Verify** rows to **Done** with release tag.

---

## Related

- `docs/parity-gap-matrix.md` — source matrix
- `docs/product-contract.md` — contract routing
- `docs/release-policy.md` — gates
- `docs/roadmap.md` — strategic deferrals
