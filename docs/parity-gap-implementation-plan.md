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
| `server_catalog` | Done | Resource `roslyn://server/catalog` (`ServerResources.GetServerCatalog`), `ServerSurfaceCatalog` + `CatalogVersion`. **Verify:** diff JSON against `docs/product-contract.md` before tagging. |
| `server_info` + tiers | Done | `ServerTools`, `ServerSurfaceCatalog` stable/experimental labels; **Verify:** spot-check `server_info` and catalog after surface changes. |
| Explicit stable vs experimental surface | Done | `docs/product-contract.md`, `ServerSurfaceCatalog`, catalog JSON. **Verify:** no experimental tool promoted without doc + release note. |
| Wrapper/integration tests for under-tested tool families | Done | Added: `WorkspaceToolsIntegrationTests`, `RefactoringToolsIntegrationTests`, `ValidationToolsIntegrationTests`, `WorkspaceResourceTests`, `PromptSmokeTests` (sample solution). **Verify:** run full `dotnet test RoslynMcp.slnx` before each release; extend when adding tools. Catalog parity remains covered by `SurfaceCatalogTests`. |
| Workspace/session limits and failed-load cleanup | Done | `WorkspaceManagerOptions` (`MaxConcurrentWorkspaces` default 8, `MaxSourceGeneratedDocuments` 500), `WorkspaceManager` semaphore + slot release, `WorkspaceExecutionGate` workspace validation and bounded gates. **Verify:** env table in `ai_docs/runtime.md`. |
| Bounded related-test scans and command timeouts | Done | `ValidationServiceOptions` (build/test timeouts), `WorkspaceExecutionGate` per-request timeout, `TestRunnerService`, `TestDiscoveryService` related-test limits. **Verify:** defaults match ops expectations for largest target solution. |
| Canonical CI and publish verification path | Done | `.github/workflows/ci.yml`, `eng/verify-release.ps1`, `eng/verify-ai-docs.ps1` (see `CI_POLICY.md`). **Verify:** artifacts `host-stdio-publish`, `release-manifests` on green `main`. |
| Documented compatibility, deprecation, release policy | Done | `docs/release-policy.md`, `README.md`, `AGENTS.md`. **Verify:** version bump + changelog entry match release. |

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
