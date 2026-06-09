<!-- purpose: Design spec for lowering the Roslyn-MCP first-hop activation cost (auto-resolve/auto-load workspace for read-only tools). Anchor for backlog rows workspace-id-omitted-single-resolve / workspace-auto-load-on-demand / workspace-id-optional-readonly-surface-flip. -->
<!-- scope: in-repo -->

# Design — Workspace auto-load on demand (lower the first-hop activation cost)

**Status:** approved design (brainstorm 2026-06-09); not yet planned/implemented.
**Origin:** 2026-06-08 multisession retro (`ai_docs/reports/20260608T203050Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`) → row `roslyn-mcp-cross-repo-steering-gap`. Cross-repo roslyn use is real but rare; the deterrent is the *explicit step* (call `workspace_load`, capture an opaque `workspaceId`, pass it) — not raw latency.

## Goal
A read-only semantic tool can be called with **no `workspaceId`** and Just Works: resolve to the loaded workspace, or auto-load a discovered one. Remove the separate load step + opaque-token juggling that steers agents to Edit/Grep/dotnet.

## Locked decisions (brainstorm)
| # | Decision | Choice |
|---|---|---|
| 1 | Problem being fixed | Explicit-step friction (not raw latency, not steering-only) |
| 2 | No-workspace behavior | Silent auto-load **+ guardrail** (refuse to guess when ambiguous/none) |
| 3 | Tool scope | Read-only, non-destructive only (`ReadOnly && !Destructive` — the existing elicitation allowlist) |
| 4 | Rollout | Default-on; additive (no existing caller breaks); `CHANGELOG` "Changed/Added" |

## Resolution precedence (applied in the read-path chokepoint)
1. Explicit `workspaceId` passed → use it (**unchanged**; existing callers unaffected).
2. Omitted + exactly **one** workspace loaded → use it.
3. Omitted + **≥2** loaded → fast-fail listing loaded ids (can't guess).
4. Omitted + **none** loaded → auto-discover: **unique** candidate → load (with progress) + retry; **ambiguous/none** → guided fast-fail (+ elicitation fallback when client supports it).

## Architecture — mechanics vs steering (the key split)
- **Mechanics (cheap, centralized):** the resolution + auto-load logic lives in **one chokepoint** — `StructuredCallToolFilter` (already does workspaceId-recovery + elicitation) and/or the read gate `gate.RunReadAsync(workspaceId, …)`. ~2-3 files, **no per-tool surface change**. The existing elicitation recovery proves the middleware already receives omitted-`workspaceId` calls.
- **Steering (the hotspot):** flip `workspaceId` **required → optional** (`string workspaceId` → `string? workspaceId = null`) across the ~49 read-only tool methods + update `[Description]` to invite omission + regenerate catalog partials. This is what makes agents *proactively* omit `workspaceId` — without it the schema still says "required" and the mechanics stay dormant. It is the **catalog hotspot** (RMCP001/RMCP002 surface analyzers) — isolate, sub-batch by Tools file, ≤1 catalog-touching initiative per sweep wave.

## Phase 2 discovery + guardrail
- **File-anchored tools** (have `filePath`): walk up from `filePath` to nearest `.sln`/`.slnx`, else nearest `.csproj`. Deterministic → load.
- **Query-anchored tools** (no path, e.g. `symbol_search`): scan sanctioned roots / session CWD (bounded: top level + one level down) for `.sln`/`.slnx`. **Open item — confirm the server reliably knows the repo root here; if not, fast-fail.**
- **Exactly one candidate** → load. **Zero or ≥2** → guided fast-fail naming candidate(s) with a ready-to-run `workspace_load(path=…)`.

## Behavior
- **Latency/cancellation:** auto-load reuses `WorkspaceManager.LoadAsync` (≈167), which emits progress (`validating-path`→`opening-workspace`→`checking-restore`) and honors the caller `CancellationToken` — visible + cancellable, not a silent hang.
- **Concurrency/limits free:** reusing `LoadAsync` inherits dedup, the 16-slot cap, LRU/strict eviction. No new concurrency code.
- **Fast-fail envelope:** standard structured error (`category`/`message`/`schemaHint`) carrying discovered candidate path(s).
- **Observability (closes the retro gap):** add to `_meta`: `autoResolution: "explicit" | "single-workspace" | "auto-loaded" | "fast-fail"` + `autoLoadElapsedMs`. This is the signal that lets us *measure* whether reach actually increased — the thing the retro couldn't see.

## Rows (dependency-ordered; all point here)
| Row id | pri | dep | scope |
|---|---|---|---|
| `workspace-id-omitted-single-resolve` | Medium | none | Chokepoint resolution: omitted id + 1 loaded → resolve; ≥2 → fast-fail; introduce `_meta.autoResolution`. No surface change. |
| `workspace-auto-load-on-demand` | Medium | `workspace-id-omitted-single-resolve` | Omitted id + none loaded → discover + auto-load (read-only allowlist) + guardrail fast-fail; extend `_meta.autoResolution:"auto-loaded"`. |
| `workspace-id-optional-readonly-surface-flip` | Low | both above | Catalog hotspot: `workspaceId` required→optional on read-only allowlist + descriptions + catalog + CHANGELOG. The steering linchpin; sub-batch by Tools file. |

> Mechanics (rows 1–2) are **dormant without the flip** (row 3) — the three form one feature; none is independently high-value. Sequence via `/backlog-sweep:prepare`.

## Non-goals (explicit)
Mutating/preview/apply tools (they get guided fast-fail, never silent guess-then-write); background warming; multi-solution auto-picking; any latency optimization of the load itself; HTTP/daemon work (`workspace-process-pool-or-daemon` is a separate Deferred concern).

## Test strategy (red-first)
Failing tests first, one per precedence branch: omit-id + 1 loaded → resolves; omit-id + 2 loaded → fast-fail names both; omit-id + none + 1 `.sln` from `filePath` → auto-loads + `_meta.autoResolution:"auto-loaded"`; omit-id + none + multiple `.sln` → fast-fail lists candidates, loads nothing; omit-id + none + no solution → fast-fail, no hang; **mutating** tool + omit-id → guided fast-fail, never auto-loads; explicit `workspaceId` unchanged (regression); catalog/genericity test → read-only allowlist exposes `workspaceId` optional, mutating keep required.

## Open items (resolve at plan time)
1. Does the server reliably know the repo root for query-anchored discovery (sanctioned-roots default / session CWD)? Fall back to fast-fail if not.
2. Exact count of read-only tools whose `workspaceId` flips (sizing the catalog hotspot) — `workspaceId` is per-method (~49 files), null-handling centralizes in the chokepoint so each method only relaxes its signature.
3. We are *betting* agents omit `workspaceId` once the schema/description invite it — `_meta.autoResolution` is how we verify the bet post-ship.

## Anchors
`src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (chokepoint; existing recovery ~253–441, `IsWorkspaceIdRecoveryAllowedFor` allowlist), read gate `gate.RunReadAsync` (e.g. `SymbolTools.cs:37`), `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:268` (`ResolveOptionalWorkspaceId` to generalize), `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:167` (`LoadAsync` reuse), `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` (flip), `src/RoslynMcp.Host.Stdio/Tools/WorkflowRecommendationTools.cs:30` (`recommend_workflow` steering).
