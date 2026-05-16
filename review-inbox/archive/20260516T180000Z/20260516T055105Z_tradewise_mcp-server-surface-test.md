# Roslyn-MCP Surface Test — TradeWise

| Field | Value |
|---|---|
| Run timestamp (UTC) | `20260516T055105Z` |
| Audited repo root | `C:\Code-Repo\TradeWise` |
| Audited entrypoint | `TradeWise.sln` (loaded from disposable worktree) |
| Operator | `darylmcd` (maintainer-detected via `gh api user`) |
| Server version | `roslyn-mcp 1.38.1+7b2c0b99c2194858a41bdaedd4b7f4538f0a0d71` |
| Server runtime | `.NET 10.0.8`, Windows 10.0.26200 |
| Roslyn version | `5.3.0.0` |
| Catalog version | `2026.04` |
| Live surface | tools stable=111, exp=58; resources stable=9, exp=4; prompts stable=0, exp=20; registered tools=169, resources=13, prompts=20, parityOk=`true` |
| Server stdio PID | `35484`, started `2026-05-16T05:47:38Z` |
| Tier | `--full` (default — full prompt, disposable worktree apply pass exercised, scorecard emitted) |
| Subagent dispatch | enabled (Group A = phases 1+2, Group B = phases 7+8+8b) |
| Output mode | `findings` (default) |
| Auto-file | maintainer-detected → `gh issue create --repo darylmcd/Roslyn-Backed-MCP` per non-refused finding (subject to dedup pre-check; P0/security findings refused and stdout-only) |
| Isolation | disposable worktree `C:\Code-Repo\TradeWise\.worktrees\surface-test-20260516T055105Z` on branch `mcp-server-surface-test/20260516T055105Z` (created from HEAD `4ba7250`); workspace_load targets the worktree's `TradeWise.sln`; primary checkout is read-only and never mutated |
| Isolation baseline (`git status --porcelain` against primary checkout at run start) | `?? .worktrees/` (pre-existing operator state — stale worktree dir `surface-test-20260516T052946Z` from earlier same-day run; out of scope for this audit) |
| Debug-log channel (`notifications/message`) | `partial` — Claude Code MCP layer does not surface live `Information`/`Warning` log events to the agent; tool errors arrive as structured responses. Operator must triage MCP stderr / client logs for the channel that this run cannot see. |
| `dotnet restore` precheck | completed (exit 0) on primary checkout + worktree (worktree's per-project `obj/project.assets.json` required separate restore even though shared NuGet cache had the packages) |
| `workspace_warm` | run automatically by `workspace_load(prewarm=true)` — 11/11 projects warmed, 11 cold compilations, prewarm 5202ms |
| Repo shape | 11 projects (5 src + 6 test); 753 documents; net10.0 single-target; xUnit test framework (inferred from `*.Tests` naming convention and project-reference structure); no source generators detected at this phase; DI presence TBD in Phase 1/2 read; `.editorconfig` presence TBD |

---

## Phase -1: MCP server precondition (hard gate)

| Check | Result |
|---|---|
| `mcp__roslyn__server_info` callable | PASS |
| `connection.state` reached `ready` post-load | PASS (`server_heartbeat` confirmed after `workspace_load`) |
| `surface.registered.parityOk == true` | PASS |
| Catalog resource counts match `server_info.surface` | PASS — `roslyn://server/catalog.summary` reports stable=111/exp=58/stableRes=9/expRes=4/stablePrompts=0/expPrompts=20, identical to `server_info.surface` |
| `workspace_health` post-load returns `healthy`-equivalent | **FLAG — prompt drift.** Live `workspace_health` returns `isReady` (boolean) + `restoreHint`; the prompt's Phase -1 step 5 references a `status` field with values `healthy`/`degraded`/`unhealthy` that the v1.38.1 response shape does not expose. Treated as guidance gap, not a server defect. Logged in *Improvement suggestions*. Functional equivalent: `isReady=true` after `workspace_reload` post-restore. |

---

## Phase 0: Workspace load + repo shape

### Project graph (11 projects, `_meta.elapsedMs=2`)

```
src/
  TradeWise.Domain          (no deps)
  TradeWise.Application     → Domain
  TradeWise.Infrastructure  → Application
  TradeWise.Api             → Infrastructure        (outputType=Library — see note)
  TradeWise.Workers         → Infrastructure        (outputType=Library — see note)
tests/
  TradeWise.Domain.Tests          → Domain
  TradeWise.Application.Tests     → Application
  TradeWise.Infrastructure.Tests  → Infrastructure
  TradeWise.Api.Tests             → Api
  TradeWise.Workers.Tests         → Workers
  TradeWise.Integration.Tests     → Domain, Application, Infrastructure, Api, Workers
```

All targets net10.0 single-TFM. Strict layered DAG: Domain has no inbound deps from any other src project; Integration.Tests references everything (the canonical multi-tier test arrangement).

**Initial observation (now upgraded to confirmed bug — see below):** `TradeWise.Api` and `TradeWise.Workers` showed `outputType=Library` in `project_graph`. ASP.NET Core APIs and Worker hosts are typically `Exe`, so the initial read was "maybe library projects with host wrappers elsewhere." On verification this turned out to be a **`project_graph` accuracy bug**, recorded as MCP-001 below.

### Finding MCP-001 — `project_graph.outputType` misreports SDK-defaulted Exe projects as Library [P2 / area=accuracy]

| Field | Value |
|---|---|
| Tool | `project_graph` (stable; v1.38.1) |
| Affected projects (TradeWise) | `TradeWise.Api` (SDK `Microsoft.NET.Sdk.Web`), `TradeWise.Workers` (SDK `Microsoft.NET.Sdk.Worker`) |
| Csproj ground truth | Both projects have **no explicit `<OutputType>` element**; both SDKs default `OutputType=Exe` |
| `project_graph.outputType` (wrong) | `Library` for both |
| `get_msbuild_properties.properties.OutputType` (correct) | `Exe` for both |
| `evaluate_msbuild_property(OutputType).evaluatedValue` (correct) | `Exe` for both |
| Reproduction | Load any solution that contains a `Microsoft.NET.Sdk.Web` or `Microsoft.NET.Sdk.Worker` csproj with no explicit `<OutputType>` element. Call `project_graph` and `get_msbuild_properties` on the same project. Compare. |
| Severity rationale | P2: silent inaccuracy in a stable tool. Agents relying on `project_graph.outputType` to decide "is this an executable?" (e.g., to choose `dotnet run` vs `dotnet build`, to decide whether to scaffold a host, to filter test-vs-host projects) get a wrong answer for the most common modern ASP.NET / Worker project shape. No data loss, no security impact, but a high-confusion correctness defect. |
| Proposed fix sketch | When `project_graph` populates `outputType`, evaluate the property through Microsoft.Build.Evaluation (the same code path `evaluate_msbuild_property` already uses correctly), not by reading the raw `<OutputType>` XML element. SDK-default values flow through evaluated properties; raw XML inspection misses them. |
| Not refused | P2, area=accuracy — Phase 19 will auto-file via `gh issue create` after dedup pre-check. |

### Performance baseline (Phase -1 / 0 reads)

| Tool | Call | `elapsedMs` | `queuedMs` | `heldMs` | Notes |
|---|---|---:|---:|---:|---|
| `server_info` | initial precondition | n/a (not surfaced) | — | — | response carried no `_meta` block |
| `server_heartbeat` | post-load readiness | n/a (not surfaced) | — | — | response carried no `_meta` block |
| `workspace_load` | TradeWise.sln, prewarm=true | 10898 | 10 | 16092 | **heldMs(16092) > elapsedMs(10898)** — possibly because prewarm time is included in heldMs but not elapsedMs; cross-check in Phase 18. Cold-load + prewarm of 11-project / 753-doc solution. |
| `workspace_list` | post-load | n/a (not surfaced) | — | — | response carried no `_meta` block |
| `workspace_health` | post-load | n/a (not surfaced) | — | — | response carried no `_meta` block |
| `project_graph` | post-load | 2 | 0 | 2 | trivially fast — pure metadata read |

**FLAG — Performance `_meta` inconsistency (Phase -1 / 0 candidate):** The prompt's cross-cutting principle #3 states "Every v1.8+ response carries `_meta.elapsedMs`" but at least four read tools in the workspace-bootstrap path (`server_info`, `server_heartbeat`, `workspace_list`, `workspace_health`) returned no `_meta` block at all on v1.38.1. Either the principle's universality is outdated, or these tools are missing the `_meta` instrumentation. Confirm or reject in Final surface closure after seeing the full sample.

### Live-surface drift detection (Phase 0 step 14)

Catalog enumerated via `roslyn://server/catalog` (counts) + `roslyn://server/resource-templates` (13 resource templates). The two output buckets:

- **`guidance gap` candidates (names in catalog never mentioned in the prompt's phase guidance):** to be enumerated in Final surface closure when the full coverage ledger is closed.
- **`prompt drift` candidates (names mentioned in the prompt absent from the live catalog):** none detected so far in Phase -1/0. Phase 5 referenced `analyze_snippet` `kind` values `expression`/`program`/`statements`/`returnExpression` — schema confirmation deferred to Phase 5.
- Open prompt-drift finding: `workspace_health` status-field shape (recorded in Phase -1 row above).

### Workspace reload post-restore

| Field | Pre-restore reload | Post-restore reload |
|---|---|---|
| `workspaceVersion` | 1 | 2 (incremented as expected) |
| `restoreRequired` | true | false ✓ |
| `documentCount` | 753 | 759 (+6, matches "+~3 per project for SDK-generated implicit-usings/AssemblyInfo" documented in `workspace_load` description) |
| `isReady` (workspace) | false | true (inferred — subsequent semantic tools resolved cleanly) |
| `_meta.elapsedMs` | n/a (post-load implied) | 4416 |
| `_meta.heldMs` | n/a | 8829 (heldMs ~2× elapsedMs — pattern continues) |

---

## Phase 0.5: Subagent dispatch decision + coverage-ledger header

Dispatch plan (per `prompts/phases/setup-and-analysis.md` Phase 0.5):

| Group | Phases | Mode | Status |
|---|---|---|---|
| G1 | 1, 2 (diagnostics + metrics) | background `audit-phase-runner` | dispatched (agentId tracked, awaiting `<RESULT>` envelope) |
| G2 | 3, 4 (symbol + flow analysis) | inline (orchestrator runs on non-overlapping tool families while G1 runs) | in progress |
| G3 | 5 (snippet) | inline | scheduled |
| G4 | 6 sub-phases (apply chains on worktree) | inline orchestrator-owned (worktree lifecycle must not leak) | scheduled |
| G5 | 7, 8, 8b (build/test/concurrency) | background `audit-phase-runner` after Phase 6 | scheduled |
| G6 | 10, 11, 12 (file ops + semantic search + scaffolding) | inline | scheduled |
| G7 | 13, 14 (project mutation + nav) | inline | scheduled |
| G8 | 15, 16, 17 (resources + prompts + negative testing) | inline | scheduled |

Phase 9 (`revert_last_apply`) runs after Phase 10 (per the canonical run order — so the revert doesn't undo Phase 6 work).

Coverage ledger column model: `kind | name | tier | category | status | phase | lastElapsedMs | notes`. Will be reconciled in Final surface closure against catalog enumeration (169 tools / 13 resources / 20 prompts).

---

---

## Phase 1 + Phase 2 — Group A subagent results (pasted verbatim, structured)

**Dispatch:** `audit-phase-runner` Group A. Duration: 3min23s. Total tool invocations: 19 distinct. 0 timeouts, 0 "No such tool".

### Phase 1 — Broad diagnostics scan

| Tool | Probe | Result | `elapsedMs` |
|---|---|---|---:|
| `project_diagnostics` | `summary=true` (cold) | FLAG (budget breach) — 0E/0W/53I; CA1506×29, SYSLIB1045×12, CA1502×8, CA1505×2, ASP0025/27×1 each | 35297 |
| `project_diagnostics` | default | PASS — same totals, returned all 53 in one page (cache hit) | 5 |
| `project_diagnostics` | `projectName=TradeWise.Application` | PASS — narrowed to 1 (CA1502 PeadValidationReportRenderer.cs:42); filter scope respected | 56 |
| `project_diagnostics` | `severity=Error` (invariant probe) | **PASS — v1.8+ contract upheld.** `totalInfo=53`, `totalErrors=0` preserved; only returned-array narrowed | 125 |
| `compile_check` | default | PASS — 0 CS diagnostics, 11/11 projects | 131 |
| `compile_check` | `severity=Error` | PASS — same | 61 |
| `compile_check` | `emitValidation=true` | FLAG (docs precision) — 0 diagnostics, 5383ms = ~41× default; description claims 50–100× | 5383 |
| `security_diagnostics` | (default) | PASS — 0 findings; SecurityCodeScan.VS2019 detected | 3013 |
| `security_analyzer_status` | (default) | PASS — netAnalyzers+SecurityCodeScan present, `missingRecommendedPackages=[]` | 2932 |
| `nuget_vulnerability_scan` | (default) | PASS — 0 vulns across 11 projects, `includesTransitive=false`. Near top of 15s scan budget. | 23562 |
| `list_analyzers` | default | PASS — `analyzerCount=30`, `totalRules=492`, no LOAD_ERROR | 62 |
| `list_analyzers` | `projectName=TradeWise.Domain` | PASS — project-scoped filter works | 5 |
| `diagnostic_details` | CA1506 @ Program.cs:2518 | PASS — accurate location, helpLinkUri valid, supportedFixes=[] (expected per CA-rule limitation) | 97 |

**Counts cross-check:** `project_diagnostics(total=53)` vs `compile_check(total=0)` match by design (project_diagnostics includes analyzers; compile_check is CS-only).

### Phase 2 — Code quality metrics

| Tool | Probe | Result | `elapsedMs` |
|---|---|---|---:|
| `get_complexity_metrics` | `minComplexity=11` | PASS — 20 hotspots, top match CA1502 diagnostics | 216 |
| `get_cohesion_metrics` | `minMethods=3` | PASS — 20 types; `BacktestEngine` LCOM4=6 largest | 201 |
| `get_coupling_metrics` | (default) | FLAG (budget breach + possibly-stale backlog) — 10 types returned, all `instability=1`; tool IS exposed (backlog row `coupling-metrics-tool` may be obsolete) | 6125 |
| `find_unused_symbols` | `includePublic=false` | **FLAG (false-positive class)** — 20 hits, ALL `Build*CommandTextForTest`, all `confidence=high`. These are test-bridge accessors used via `InternalsVisibleTo` / reflection. Detector misses the `*ForTest` convention. | 3312 |
| `find_unused_symbols` | `includePublic=true` | PASS — 15 hits, mix high/medium/low (`UserPreferences`, `RawDocument`, `Security`, `ModelDefinition` etc. — Domain records often EF-shaped, medium confidence appropriate) | 1776 |
| `find_duplicated_methods` | (default) | PASS — 10 clusters; largest is 15-member `NormalizeRelativeEndpointPath` across FMP providers (real TradeWise refactor opportunity); verified one example at `FmpAnalystGradesMarketDataProvider.cs:233` | 563 |
| `find_duplicate_helpers` | (default) | PASS — 2 hits, both `confidence=medium`, no obvious BCL false-positives | 93 |
| `find_duplicated_code` | (default) | **FLAG (schema vs alias drift)** — returned `count=5` vs `find_duplicated_methods.count=10` with identical default `limit=50` and same workspace; alias appears to apply a smaller hidden limit / different cutoff | 210 |
| `find_dead_locals` | (sample methods) | PASS — 9 hits, all in test fixtures (plausible) | 4695 |
| `find_dead_fields` | (default) | PASS — 1 hit (`_securityId` in `FakeAnnouncementWriterWithMemory`), `safelyRemovable=false` populated via `removalBlockedBy ConstructorWrite` marker | 4687 |
| `get_namespace_dependencies` | `circularOnly=true` | PASS (real repo finding) — 2 circular deps detected in `TradeWise.Infrastructure`: Persistence ↔ Dashboard ↔ Scoring ↔ MarketData.Fmp ↔ Persistence | 357 |
| `get_nuget_dependencies` | `summary=true` | **FLAG (schema drift)** — 29 packages, all return `version: "centrally-managed"` placeholder instead of resolved version from `Directory.Packages.props`; loses real version info for audit purposes | 1633 |
| `suggest_refactorings` | (default) | PASS — 10 ranked complexity items; recommendedTools (`analyze_data_flow`, `extract_method_preview/apply`, `compile_check`) match live surface; sensible ranking | 1472 |

**Top complexity hotspots (TradeWise repo finding):**
- `tests/.../MomentumSanityValidationTests.cs:978` — cyclomatic 82 — `GetOperandSize` (test bloat — extraction-or-split candidate)
- `src/TradeWise.Infrastructure/MarketData/Fmp/FmpOptions.cs:91` — 30 — `FromConfiguration`
- `src/TradeWise.Infrastructure/Persistence/PeadValidationRepository.cs:835` — 28 — `MapRun`

**Top LCOM4 candidates (TradeWise):** `BacktestEngine` (6), `NyseTradingCalendar` (4), `DashboardRequest` (3).

### MCP server findings surfaced by Group A

| Finding id | Severity | Area | Source tool | One-line |
|---|---|---|---|---|
| **MCP-001** | P2 | accuracy / tools | `project_graph` | Reports `outputType=Library` for SDK-defaulted Exe projects (Web/Worker SDKs). Detailed above. |
| **MCP-002** | P3 | tools | `find_duplicated_code` alias | Returns fewer results than `find_duplicated_methods` with identical `limit=50`; alias has a hidden lower cutoff. Test: same workspace, same default limit → 5 vs 10. |
| **MCP-003** | P3 | tools / schema | `get_nuget_dependencies(summary=true)` | Returns `version: "centrally-managed"` placeholder instead of resolved version from `Directory.Packages.props`. Audit consumers (security/compliance gates) get useless version data. |
| **MCP-004** | P3 | tools / false-positive class | `find_unused_symbols(includePublic=false)` | Convention-invoked detector misses the `*ForTest` shape. TradeWise has 20 `Build*CommandTextForTest` accessors all flagged `confidence=high` despite being test-bridge methods used via `InternalsVisibleTo`/reflection. Suggested fix: add `*ForTest` / `*Internal` / `*_ForTesting` to the convention-invoked allowlist alongside the existing patterns. |
| **MCP-005** | P2 | perf | `project_diagnostics(summary=true)` cold-start | 35.3s on the TradeWise 11-project/759-doc solution exceeds the prompt's 15s solution-scan budget. Warm cache reduces to 5ms. Possibly the SCS (SecurityCodeScan) analyzer's first-time AST walk. Worth profiling. |
| **MCP-006** | P3 | docs | `compile_check(emitValidation=true)` description | Description claims "50–100× slower than GetDiagnostics-only on large solutions" with restored packages; observed ~41× on TradeWise (under the floor). Either the floor is generous or the wording should be "tens to ~100×". |
| **MCP-007** | P3 | docs / backlog | `get_coupling_metrics` | Tool IS exposed and works (returns ranked instability metrics). Backlog row `coupling-metrics-tool` may be stale — recommend the maintainer audit the backlog for similar "claimed missing but actually present" rows. |

### TradeWise repo findings (for user, NOT MCP-server defects)

These are real opportunities surfaced in the audited code — separate from MCP server bugs. The user (`darylmcd`) can fold them into the TradeWise backlog:

- **TW-001 (refactor candidate):** 15-FMP-provider `NormalizeRelativeEndpointPath` duplication cluster (`FmpAnalystGradesMarketDataProvider.cs:233` and 14 siblings). Extract to a single helper.
- **TW-002 (architecture):** Circular namespace dependencies inside `TradeWise.Infrastructure`: `Persistence ↔ Dashboard ↔ Scoring ↔ MarketData.Fmp ↔ Persistence`. Worth a layering review per the user's "quality first until production" stance.
- **TW-003 (complexity):** `MomentumSanityValidationTests.GetOperandSize` cyclomatic 82, `FmpOptions.FromConfiguration` 30, `PeadValidationRepository.MapRun` 28. Each above CA1502 default and above the CA1502 suggestion-override the .editorconfig sets — but they still exceed reasonable thresholds.
- **TW-004 (dead code in tests):** `_securityId` field in `FakeAnnouncementWriterWithMemory` (test fixture).

---

## Pending sections

- Phase 0.5: coverage ledger seed + drift detection
- Phase 1: broad diagnostics scan (Group A subagent)
- Phase 2: code quality metrics (Group A subagent)
- Phase 3: deep symbol analysis (inline)
- Phase 4: flow analysis (inline)
- Phase 5: snippet & script validation (inline)
- Phase 6: apply-tool exercise on disposable worktree (inline orchestrator-owned)
- Phase 7+8+8b: configuration + build/test + concurrency (Group B subagent)
- Phase 9+10: revert_last_apply + file/project ops (inline)
- Phase 11-15: semantic search, scaffolding, project mutation, navigation, resources
- Phase 16-18: prompts, boundary tests, regression verification
- Final surface closure: coverage ledger reconciliation + promotion scorecard
- Phase 19: finding emission (auto-file path active per maintainer detection)
