# Backlog sweep plan — 20260609T134405Z

**Scope:** the 3 `workspace-auto-load-on-demand` feature rows (operator-scoped). Design spec: `ai_docs/items/workspace-auto-load-on-demand-design.md`.

**Sequencing:** strict dependency chain `1 → 2 → 3`. All three touch `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`, so they are **fully serial** (no parallel wave). Initiative 3 is the catalog **hotspot** (`ServerSurfaceCatalog.*`). Orders 2–3 partly anchor on code order 1 introduces — see per-stanza notes.

<!-- BSWEEP:STATUS-TABLE -->

---

### 1. workspace-id-omitted-single-resolve

| Field | Content |
|---|---|
| Diagnosis | `ResolveOptionalWorkspaceId` (`WorkspaceTools.cs:560-570`) already implements the single-workspace-auto-resolve / ≥2-fast-fail logic, but it is a `private static` helper scoped to `WorkspaceTools` and only wired for `workspace_readiness_report` (+1 sibling). `StructuredCallToolFilter` (`StructuredCallToolFilter.cs:296-441`) has a recovery stack (`IsWorkspaceIdRecoveryAllowedFor`, `TryRecoverMissingWorkspaceIdAsync`) that fires on the exception path but has no pre-dispatch resolution that intercepts a null/omitted `workspaceId` before the SDK binder throws. `_meta` (`GateMetricsDto.cs:57-67`, `AmbientGateMetrics.cs:54-111`) has no `autoResolution` field. Root cause: the resolver was introduced in-tool, not in the chokepoint, so it can't be applied uniformly. |
| Approach | 1. Promote `ResolveOptionalWorkspaceId` (`WorkspaceTools.cs:560`) from `private static` → `internal static` (no signature change). 2. Add `AutoResolution` to `GateMetricsBuilder` (`AmbientGateMetrics.cs`) + a `string? AutoResolution = null` positional param on `GateMetricsDto`. Values: `explicit` / `single-workspace` / `fast-fail`. 3. In `StructuredCallToolFilter.Create` (`:116`), before `next(...)`: if tool ∈ `IsWorkspaceIdRecoveryAllowedFor` allowlist AND `workspaceId` arg null/absent → enumerate loaded workspaces; exactly 1 → patch id + set `autoResolution=single-workspace`; ≥2 → structured fast-fail (same `category`/`message`/`schemaHint` envelope) listing ids + `autoResolution=fast-fail`; id present → `autoResolution=explicit`, proceed. Read-only allowlist only. No per-tool signature change. |
| Scope | Production (4): `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`, `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`, `src/RoslynMcp.Core/Services/AmbientGateMetrics.cs`, `src/RoslynMcp.Core/Models/GateMetricsDto.cs`. Tests (1 new): `tests/RoslynMcp.Tests/StructuredCallToolFilterResolutionTests.cs`. Rule 3 hard ceiling: 4 production files (no exemption). |
| Tool policy | edit-only |
| Estimated context cost | 45000 |
| Risks | (1) Verify `IWorkspaceManager` resolves in the filter's DI scope before assuming; else no-op fallback to existing binder/recovery path. (2) Pre-dispatch arg patch must mirror the existing `DispatchWithTemporaryArgumentsAsync` save/restore idiom (`:503-523`). (3) Adding a positional param to the `GateMetricsDto` sealed record may need test-callsite fixups — verify via `compile_check`. (4) Fast-fail must reuse the existing structured envelope, not a new shape. (5) `workspace_readiness_report`'s own null-id block (`:274-295`) stays — it handles the no-workspace-loaded branch the filter must NOT silently resolve. |
| Validation | Red-first `StructuredCallToolFilterResolutionTests.cs`: omit id + 1 loaded → resolves + `_meta.autoResolution=single-workspace`; omit + 2 loaded → fast-fail names both ids + `fast-fail`; explicit id → passthrough + `explicit`; mutating tool + omit id → no pre-dispatch resolution. Existing `WorkspaceReadinessReportTests` unchanged. `compile_check` per edit; targeted `test_run`. `./eng/verify-ai-docs.ps1`. |
| Performance review | N/A — correctness; happy-path adds an O(n≈1) loaded-workspace enumeration, not a hot path. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added workspace-id auto-resolution in the read-path middleware: a read-only tool called with `workspaceId` omitted resolves to the single loaded workspace, or fast-fails listing ids when ≥2 are loaded. New `_meta.autoResolution` (`explicit`\|`single-workspace`\|`fast-fail`) emitted on read-only calls. No schema change; explicit-id callers unaffected. |
| Backlog sync | Close rows: [workspace-id-omitted-single-resolve]. |

_Deepener: judgmentHeavy — promote-vs-duplicate resolver choice (chose promote); GateMetricsDto positional-record extension may need test fixups; 4 files across two projects._

### 2. workspace-auto-load-on-demand

| Field | Content |
|---|---|
| Diagnosis | The chokepoint recovery path (`StructuredCallToolFilter.cs:253-441`) handles omitted `workspaceId` via elicitation (`IsWorkspaceIdRecoveryAllowedFor` `:361`, `TryRecoverMissingWorkspaceIdAsync` `:381`). The gap is the **zero-workspaces-loaded** case: no code discovers a solution from the call context and auto-loads it. No discovery helper exists; `ResolveOptionalWorkspaceId` only resolves among already-loaded sessions. `_meta.autoResolution` is introduced by order-1 (does not exist at HEAD). `WorkspaceManager.LoadAsync` (`WorkspaceManager.cs:180+`) already gives dedup, 16-slot cap, LRU/strict eviction, progress — reuse without new concurrency code. |
| Approach | 1. New `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs`: (a) file-anchored — if args carry a `filePath`-like key, walk up to nearest `.sln`/`.slnx` else `.csproj`; (b) query-anchored — fetch declared client roots via `server.RequestRootsAsync` (mirrors `ClientRootPathValidator`), scan top level + one level down for `.sln`/`.slnx`. Exactly 1 → return it; 0 or ≥2 → null. **This resolves the design's open spike**: use declared sanctioned roots, NOT a CWD assumption; if none declared and no `filePath`, return null → hard-fail rather than guess. 2. Extend the `IsWorkspaceIdRecoveryAllowedFor` branch (`:296`): before eliciting, if `ListWorkspaces()` empty → `SolutionDiscoveryHelper.TryDiscoverAsync`; unique → `LoadAsync` + patch id + dispatch (mirror `:416-440` load-and-retry); null → guided fast-fail naming candidates. Mutating/preview/apply never reach the gate (`ReadOnly && !Destructive`) → invariant structurally enforced. 3. Add `autoResolution="auto-loaded"` + `autoLoadElapsedMs` (Stopwatch). |
| Scope | Production (3): `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`, `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs` (new), `src/RoslynMcp.Core/Models/GateMetricsDto.cs` (only if order-1 hasn't already added `autoLoadElapsedMs`). Tests (2 new): `StructuredCallToolFilterAutoLoadTests.cs`, `SolutionDiscoveryHelperTests.cs`. Rule 3: ≤4 prod. `WorkspaceManager.cs` is a hotspot — this initiative touches it only via the existing `LoadAsync` call (no edit); keep it out of any shared wave anyway. |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) **Order-1 dep**: `_meta.autoResolution` must exist before this compiles cleanly — branch atop order-1 or stub + coordinate at merge. (2) Query-anchored discovery: if client declares no roots, `RequestRootsAsync` returns empty → fast-fail cleanly, never hang. (3) Discovery latency (RPC + bounded FS scan) on the cold path only; honor `CancellationToken`, no unguarded blocking. (4) No catalog surface change → RMCP analyzers untouched. (5) Dormant without order-3 (agents won't omit until schema optional). |
| Validation | `dotnet build -c Release -p:TreatWarningsAsErrors=true`; `compile_check` per edit. Red-first `StructuredCallToolFilterAutoLoadTests`: omit id + 0 loaded + 1 `.sln` from filePath → auto-loads + `autoResolution=auto-loaded` + `autoLoadElapsedMs>0`; 2 `.sln` → fast-fail names both, nothing loaded; no solution → fast-fail no hang (timeout guard); mutating tool + omit id → guided fast-fail, count unchanged. `SolutionDiscoveryHelperTests`: file-anchored walk-up; query-anchored with declared roots; zero roots → null. Regression: existing elicitation tests still pass (new branch only when 0 loaded). |
| Performance review | N/A — cold-path-only (entered only when zero workspaces loaded). |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added automatic workspace discovery + on-demand load for read-only tools: with `workspaceId` omitted and none loaded, the server discovers the nearest `.sln`/`.slnx` (file-anchored via `filePath` walk-up, or query-anchored via declared client roots) and auto-loads before retrying. Ambiguous/missing → structured fast-fail. Mutating/preview/apply excluded. |
| Backlog sync | Close rows: [workspace-auto-load-on-demand]. |

_Deepener: partly design-anchored (extends order-1 code absent at HEAD); judgmentHeavy — query-anchored discovery depends on client-declared roots (spike resolved this way); GateMetricsDto file may be unnecessary if order-1 adds the field._

### 3. workspace-id-optional-readonly-surface-flip

| Field | Content |
|---|---|
| Diagnosis | All four pilot tools (`symbol_search` `:23`, `find_references` `:240`, `document_symbols` `:353`, `go_to_definition` `:174`) live in `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`, each declaring `[Description(...)] string workspaceId` (no default). `ToolParameterIndex.BuildSchema` (`ToolParameterIndex.cs:100`) derives `Required` from `!HasDefaultValue` → all four are `Required:true`. `IsWorkspaceIdRecoveryAllowedFor` (`StructuredCallToolFilter.cs:361-379`) gates on `Required:true` — after the flip, the elicitation-recovery path STOPS firing for these four; order-1's null-aware `gate.RunReadAsync` resolution handles null directly (intentional). `ServerSurfaceCatalog.Symbols.cs` carries only tool-level metadata (no per-parameter schema) → **no catalog regeneration needed**. Without the flip, orders 1–2 stay dormant (agents see required, always supply it). |
| Approach | Scope to the PILOT subset only (4 tools, all in `SymbolTools.cs`). (1) Flip each `workspaceId` to `string? workspaceId = null`. (2) Update each `[Description]` to invite omission + state the order-1 middleware dependency. (3) Verify tool bodies pass the now-nullable `workspaceId` unchanged to `gate.RunReadAsync` / `IWorkspaceManager` (null-resolution is order-1's responsibility) — no body logic change. Test: `tests/RoslynMcp.Tests/WorkspaceIdOptionalSurfaceTests.cs` reflecting over `ToolParameterIndex` to assert the 4 pilot tools expose `workspaceId` `Required:false` and a sample of mutating tools keep `Required:true`. Emit `changelog.d/workspace-id-optional-readonly-surface-flip.md` (Changed). **DEFER** the remaining ~45 read-only tool methods (other `*Tools.cs`) to a follow-on, sub-batched by file, gated on the pilot's `_meta.autoResolution` adoption signal. |
| Scope | Production: 1 (`src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`). Tests: 1 new (`WorkspaceIdOptionalSurfaceTests.cs`). `changelog.d/*` fragment not counted. `ServerSurfaceCatalog.Symbols.cs` NOT touched (no per-param schema). Rule 3 exemption: tool-surface-only, 1 file. Deferred remainder ~45 methods scoped OUT. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) **Elicitation-gate interaction (critical)**: post-flip `IsWorkspaceIdRecoveryAllowedFor` (`:377` `Required:true`) won't fire for the 4 — intentional, but executor MUST confirm order-1 shipped first (else defer, or verify `gate.RunReadAsync` handles null). (2) `go_to_definition` `:174` uses `IWorkspaceManager` directly alongside the gate — confirm null handled there too post-order-1. (3) Published-surface change → CHANGELOG + ADR-lite (Directive #4); additive (explicit-id callers unaffected). (4) Executor must NOT expand beyond the pilot. (5) No fanout (4 methods, one file). |
| Validation | `dotnet build -c Release -p:TreatWarningsAsErrors=true` (RMCP001/002 pass — catalog unchanged). `WorkspaceIdOptionalSurfaceTests` via `test_run --filter WorkspaceIdOptionalSurface`: 4 pilot tools `Required:false`, mutating tools `Required:true`. `compile_check` per edit. Manual: `symbol_search` w/o `workspaceId` + 1 loaded → `_meta.autoResolution=single-workspace`; + 2 loaded → fast-fail both ids (needs order-1 active). `./eng/verify-ai-docs.ps1`. |
| Performance review | N/A — parameter default introduction has zero runtime cost. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Changed: `workspaceId` is now optional (defaults to `null`) on `symbol_search`, `find_references`, `document_symbols`, `go_to_definition`. With one workspace loaded, callers may omit it and the server resolves automatically. Explicit-id callers unaffected. Pilot subset; full read-only sweep is a follow-on. |
| Backlog sync | Close rows: [workspace-id-optional-readonly-surface-flip]. **File a follow-on row** for the deferred ~45-method sweep (sub-batched by Tools file), gated on the pilot adoption signal. |

_Deepener: judgmentHeavy — elicitation-gate dependency on order-1 (non-obvious correctness); catalog-exclusion is a judgment call to re-verify; deferred remainder needs a follow-on row filed in the same PR._

<!-- BSWEEP:STATUS-TABLE BEGIN — generated from state.json; do not edit by hand -->
## Status (generated)

| # | id | status | PR | rows closed |
|---|----|--------|----|-------------|
| 1 | workspace-id-omitted-single-resolve | pending | — | workspace-id-omitted-single-resolve |
| 2 | workspace-auto-load-on-demand | pending | — | workspace-auto-load-on-demand |
| 3 | workspace-id-optional-readonly-surface-flip | pending | — | workspace-id-optional-readonly-surface-flip |
<!-- BSWEEP:STATUS-TABLE END -->
