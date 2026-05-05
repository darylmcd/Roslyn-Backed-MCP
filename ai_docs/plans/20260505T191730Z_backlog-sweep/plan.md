# Backlog sweep plan — 20260505T191730Z

**Generated:** 2026-05-05T19:17:30Z
**Backlog snapshot:** 2026-05-05T19:30:00Z
**Initiative count:** 17
**Anchor verification:** performed (live MCP catalog + repo-relative path checks)

This sweep covers 5 High + 8 Medium + 2 Low rows. The Low row `tool-surface-pagination-or-tool-sets` is **TRACK-only** per its own do-cell language and is excluded from the plan; revisit when its trip conditions fire (small-model friction OR surface count >= 200). Defer rows are also excluded.

The Medium row `audit-deep-skill-migration` is bundle-shaped (S3 migration + B1–B5 fixes + S2 surface-audit wiring + S5 archive script). Per the planner's heroic-bundle pre-split rule, it is split into two initiatives in this plan: a 4-file structural-unit migration (`audit-deep-skill-create-and-mode-split`) and a small follow-on (`audit-deep-archive-and-surface-audit-integration`). Both close the original row.

Sort: priority band (High → Medium → Low), cost ASC within band, deps respected, hotspot-touching initiatives non-adjacent. Hotspot map: `ServerSurfaceCatalog.*.cs` partials (initiatives #3, #5), `WorkspaceManager.cs` (initiatives #4, #10, #11), `ServiceCollectionExtensions.cs` (initiatives #2, #11). All catalog-touching pairs and WorkspaceManager-touching tuples are scheduled non-adjacent.

## Initiatives (in order)

### 1. progress-emit-audit-coverage

| Field | Content |
|---|---|
| Status | merged (PR #503, 2026-05-05) |
| Backlog rows closed | `progress-emit-audit-coverage` |
| Diagnosis | Audit-investigation initiative, not a single-bug fix. Per row: 8 tools currently take `IProgress<ProgressNotificationValue>` but coverage is uneven; the dominant blockers are `workspace_load` (~45s P95 on OrchardCore), `workspace_warm` (~17s P95), `build_workspace`, and `test_run`. Verified live: `src/RoslynMcp.Host.Stdio/Tools/ProgressHelper.cs` exists and is the canonical emission helper; `WorkspaceTools.cs` and `ValidationTools.cs` are the named anchors. The investigation is "audit each long-running tool: emits progress, or documented as 'fast enough not to need it'" — this is observable / measurable, not speculative. |
| Approach | (a) Read each cited tool's implementation and tabulate emission points (none / coarse / stage-fine). (b) For `workspace_load`, add stage emission at: evaluating-msbuild, restoring, opening N/M projects, done. (c) For `workspace_warm`, emit at: scheduling, project N/M warmed, done. (d) For `build_workspace`, emit at: msbuild target start, project N/M built. (e) For `test_run`, emit at: discovering, project N/M running, assertion summary. Helper extension to `ProgressHelper` for the new stage-label shapes. Tools that don't pass the "fast enough" bar gain emission; tools that do are documented in code comments with the elapsed-budget rationale. |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Tools/ProgressHelper.cs` (new stage-emission helpers), `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (workspace_load + workspace_warm emission points), `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` (build_workspace + test_run emission points). Test files: 1 new — `tests/RoslynMcp.Tests/Progress/ProgressEmissionTests.cs` capturing emitted stage labels via a `IProgress<>` recorder fixture. Within Rule 3 (3/4) and Rule 4 (1/3). |
| Tool policy | edit-only |
| Estimated context cost | 50000 |
| Risks | (1) `workspace_load` cold-start emits before MSBuildWorkspace finishes restoring — confirm progress messages don't depend on workspace state. (2) Test fixture must NOT timeout against a real workspace (use a recorder mock). (3) Don't break existing telemetry by reusing stage-label strings already keyed in client UIs — pick fresh-but-descriptive labels. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New `ProgressEmissionTests` asserting the expected sequence of stage labels for each of the 4 tools. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: load a >50-project solution, observe `notifications/message` for the new stage entries. |
| Performance review | N/A — observational; emission overhead is constant per stage and `IProgress` is no-op when no client subscribes. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: stage-fine progress emission for `workspace_load`, `workspace_warm`, `build_workspace`, and `test_run` — clients now receive intermediate stage labels (e.g. `evaluating msbuild → restoring → opening 47/227 projects → done`) instead of waiting silently. Closes `progress-emit-audit-coverage`. |
| Backlog sync | Close rows: [`progress-emit-audit-coverage`]. Mark obsolete: []. Update related: []. |

### 2. workspace-cache-store-infrastructure

| Field | Content |
|---|---|
| Status | merged (PR #505, 2026-05-05) |
| Backlog rows closed | `workspace-cache-store-infrastructure` |
| Diagnosis | Verified live. `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` is the consumer that the *next* row (`workspace-load-uses-cache-fast-path`) will wire — but this row explicitly does NOT touch it. The row scopes itself to the new `IWorkspaceCacheStore` contract + on-disk impl + DI registration, with `WorkspaceManager` deferred to the dependent row. OrchardCore profile evidence (`docs/large-solution-profiling-baseline.md`) shows workspace_load P95 = 44.85s — the lever this initiative starts building. |
| Approach | (a) New `src/RoslynMcp.Core/Services/IWorkspaceCacheStore.cs` with read/write/invalidate methods keyed by `(solution-hash, sdk-version, msbuild-graph-hash)`. (b) New `src/RoslynMcp.Roslyn/Services/WorkspaceCacheStore.cs` with on-disk JSON + version-tagged serialization under `~/.roslyn-mcp/cache/<solution-hash>/<sdk-version>/<msbuild-graph-hash>/`. Cached data: evaluated MSBuild project graph (project paths + references) and per-project MetadataReference list (path + mtime). NOT cached: compilation snapshots, analyzer state. (c) DI registration in `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs`. |
| Scope | Production files: 3 — Core interface, Roslyn impl, DI registration. **Rule 3 structural-unit exemption: 3 units (Core contract + Roslyn impl + Registration; no Host.Stdio tool surface for an internal service).** Mandatory addenda: TestBase.cs DI registration (4th file). Test files: 2 new — `tests/RoslynMcp.Tests/Services/WorkspaceCacheStoreRoundTripTests.cs` (write→read→equality), `tests/RoslynMcp.Tests/Services/WorkspaceCacheStoreInvalidationTests.cs` (sdk-version bump invalidates). |
| Tool policy | edit-only |
| Estimated context cost | 50000 |
| Risks | (1) On-disk format becomes a compatibility surface — version-tag the file from day 1 so future format changes invalidate cleanly. (2) `~/.roslyn-mcp/cache/` directory creation must be idempotent and fall back gracefully on Windows-EPERM. (3) Don't serialize platform-specific paths verbatim — the cache must invalidate on path-style mismatch. (4) ServiceCollectionExtensions.cs is an addenda hotspot — single-line registration only, no other DI changes in this initiative. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) Round-trip + invalidation tests. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: serialize against SampleSolution, restart, deserialize, assert equality. |
| Performance review | N/A in this initiative — perf gain measured in the consumer row (`workspace-load-uses-cache-fast-path`). |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `IWorkspaceCacheStore` infrastructure — bounded persistent cache for MSBuild project graph + per-project MetadataReference list under `~/.roslyn-mcp/cache/<solution-hash>/<sdk-version>/<msbuild-graph-hash>/`. Internal service; not exposed as an MCP tool. Will be consumed by `WorkspaceManager` in a follow-on PR. Closes `workspace-cache-store-infrastructure`. |
| Backlog sync | Close rows: [`workspace-cache-store-infrastructure`]. Mark obsolete: []. Update related: []. |

### 3. tool-output-schema-infrastructure

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `tool-output-schema-infrastructure` |
| Diagnosis | Verified live. `mcp__roslyn__server_info` confirms 167 tools registered, all returning `TextContentBlock`-only. `[McpToolMetadata]` attribute exists at `src/RoslynMcp.Host.Stdio/Catalog/McpToolMetadataAttribute.cs` and is the right wiring point per the row. `StructuredCallToolFilter` (Middleware) handles `_meta` injection today. MCP spec 2025-06-18 § Tools / Structured Content requires that a tool returning `structuredContent` SHOULD also return the serialized JSON in a `TextContent` block — both channels must coexist. |
| Approach | (a) Extend `McpToolMetadataAttribute` with `outputSchemaTypeRef: Type?` parameter (nullable; tools without it skip schema emission). (b) Generate JSON Schema from the type via System.Text.Json schema-gen (or `JsonSchema.Net` if S.T.J generation insufficient — confirmed at impl time). (c) Expose generated schemas via a new `outputSchema` field on `ServerSurfaceCatalog` tool entries. (d) Update `StructuredCallToolFilter` to forward `_meta` injection across both `content[].text` and the new `structuredContent` channels — never emit one without the other when schemaTypeRef is set. (e) Optional .csproj add: `JsonSchema.Net` (only if needed). |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Catalog/McpToolMetadataAttribute.cs` (new param), `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (schema exposition; HOTSPOT — wiring only, no per-tool entries), `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (dual-channel _meta injection). Possible 4th file: `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` if `JsonSchema.Net` package add proves necessary; if S.T.J built-in suffices, stay at 3. Test files: 1 new — `tests/RoslynMcp.Tests/Middleware/StructuredContentRoundTripTests.cs` (a tool with schema set returns both `content[].text` and `structuredContent`, both validate). |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) Catalog hotspot — wiring only, no per-tool catalog entries change in this row (those land in batch initiatives). (2) System.Text.Json schema-gen exists in .NET 9+ but feature-set is narrower than `JsonSchema.Net` — need to confirm the existing DTO records (records with init-only props, nested types) round-trip cleanly. If not, add the package. (3) `_meta` injection currently runs once per response — must dedupe across the dual channels so clients don't see two `_meta` blobs. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) `StructuredContentRoundTripTests` covering: tool-with-schema returns dual-channel; tool-without-schema returns text-only (unchanged); `_meta` appears exactly once per response. (c) `./eng/verify-release.ps1 -Configuration Release` (gates README surface count via `ReadmeSurfaceCountTests` — should be unaffected; spot-check). (d) Manual: invoke `server_info` (which gets schema in initiative #5), confirm dual-channel response. |
| Performance review | Hot-path adjacent — the filter runs on every tool call. Schema-gen happens once per type at static-init time and is cached. Per-call overhead is the additional JSON serialization for the structuredContent channel. Target: P50 overhead < 1 ms; if worse, defer schema attach for high-frequency tools to batches that opt-in. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: MCP 2025-06-18 `outputSchema` + `structuredContent` infrastructure — `[McpToolMetadata]` accepts an optional `outputSchemaTypeRef`, schemas generate from existing DTO records, `StructuredCallToolFilter` emits both `content[].text` and `structuredContent` channels. No tools opted in this PR (per-tool batches follow). Closes `tool-output-schema-infrastructure`. |
| Backlog sync | Close rows: [`tool-output-schema-infrastructure`]. Mark obsolete: []. Update related: `tool-output-schema-batch-1-server-info-workspace` becomes runnable after this lands. |

### 4. workspace-load-uses-cache-fast-path

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `workspace-load-uses-cache-fast-path` |
| Diagnosis | Verified live. `WorkspaceManager.LoadIntoSessionAsync` is the canonical entry point per the row's anchor; `WorkspaceExecutionGate.cs` carries the load-gate metrics path. Cache store from initiative #2 is the prerequisite contract. Target: cut workspace_load P95 from ~45s to ~5–10s on warm-cache reload. |
| Approach | (a) `WorkspaceManager.LoadIntoSessionAsync` consults `IWorkspaceCacheStore.TryGetAsync(...)` first; on hit, skip MSBuild SDK resolution and use the persisted project-graph + metadata-reference list to seed the workspace. Semantic models still build on demand. (b) On miss, run the existing cold-load path; on success, write fresh entries to the cache before returning. (c) `WorkspaceExecutionGate` emits a `cacheHit: bool` metric so future profiling can isolate the warm-cache path. (d) No behavior change when cache is invalidated or absent — just falls through. |
| Scope | Production files: 2 — `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (HOTSPOT — `LoadIntoSessionAsync`, `LoadAsync` paths only; surgical edits, not refactoring), `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs` (cacheHit metric on the load gate). Test files: 1 new — `tests/RoslynMcp.Tests/Workspace/WorkspaceLoadCacheFastPathTests.cs` against the SampleSolution fixture (cold load → write → restart workspace → warm load < cold load timing). |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | (1) `WorkspaceManager.cs` is an addenda hotspot — surgical edits to two methods only. No reshaping. (2) Test must not be flaky on cold/warm timing — use a relative-ratio assertion (warm < 0.5 × cold) rather than an absolute threshold. (3) Cache hit must produce a workspace functionally identical to a cold load for downstream calls — assert via a follow-up `compile_check` in the test. (4) MSBuildWorkspace ownership: cache hit still creates a fresh `MSBuildWorkspace` instance; cache only seeds graph, not the workspace itself. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New cache-fast-path test. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: `eng/profile-large-solution.ps1` against OrchardCore, capture cold and warm runs, append the new row to `docs/large-solution-profiling-baseline.md` (follow-up PR; not in scope here). |
| Performance review | This IS the performance fix. Target: warm-cache `workspace_load` P95 ≤ 0.25 × cold-cache P95 against the SampleSolution fixture. If the warm/cold ratio is >0.5, cache is too coarse — escalate to redesign. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Changed: `workspace_load` consults the on-disk cache store before opening MSBuildWorkspace. Cache hits skip MSBuild SDK resolution and reuse the persisted project graph + metadata-reference list, cutting cold-start P95 toward ~5–10s on warm-cache reload. Cache miss falls through to the existing cold-load path. Closes `workspace-load-uses-cache-fast-path`. |
| Backlog sync | Close rows: [`workspace-load-uses-cache-fast-path`]. Mark obsolete: []. Update related: `workspace-cache-prewarm-on-load` becomes runnable. |

### 5. tool-output-schema-batch-1-server-info-workspace

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `tool-output-schema-batch-1-server-info-workspace` |
| Diagnosis | Verified live. The 6 tools in this batch (`server_info`, `server_heartbeat`, `workspace_status`, `workspace_list`, `workspace_health`, `workspace_drift_check`) have well-defined response DTOs already — confirmed by reading the tool methods in `ServerTools.cs`, `WorkspaceTools.cs`, `WorkspaceDriftTool.cs`. Each returns a typed object that gets JSON-serialized into the `TextContentBlock`. Once initiative #3 lands, attaching `outputSchemaTypeRef = typeof(<Dto>)` to each `[McpToolMetadata]` annotation enables the dual-channel response. |
| Approach | (a) For each of the 6 tools, identify the response DTO type (cite type names in implementation). (b) Update each `[McpToolMetadata]` annotation to set `outputSchemaTypeRef = typeof(<DtoType>)`. (c) Verify schema generation succeeds for each DTO at static-init time (or at first call). (d) Update each catalog entry in `ServerSurfaceCatalog.*.cs` partials to expose the new schema field. (e) No tool body changes; this is annotation-only wiring. |
| Scope | Production files: 4 — `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` (server_info + server_heartbeat annotations), `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (workspace_status + workspace_list + workspace_health), `src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs` (workspace_drift_check), `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs` (catalog entry schema fields; HOTSPOT — schema-only). At Rule 3's 4-file cap. Test files: 1 new — `tests/RoslynMcp.Tests/Tools/Batch1OutputSchemaTests.cs` asserting `structuredContent` field is present and schema-valid for each of the 6 tools. |
| Tool policy | edit-only |
| Estimated context cost | 60000 |
| Risks | (1) Catalog hotspot — schema-field-only edits, non-adjacent to initiative #3 which also touches catalog (positions 3 and 5 — non-adjacent). (2) DTO types must be public for `typeof(...)` to be available in the attribute — confirm visibility per type. If any are internal, either escalate visibility (this PR) or split the row to a follow-on. (3) Schema-gen failure at static-init time crashes the server — wrap in try/catch and log a `warn` so a single bad DTO doesn't prevent server startup. (4) Smaller batches (server-only or workspace-only) are an option if 4-file cap proves tight at impl time — split rather than blow the budget. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) `Batch1OutputSchemaTests` covering all 6 tools. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: invoke `server_info`, confirm the response carries both `content[].text` and `structuredContent`, schema validates. |
| Performance review | Schema-gen runs once at static-init; per-call cost is the additional JSON serialization. Spot-check `server_info` round-trip P50 — should remain < 100 ms. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `outputSchema` + `structuredContent` channel for 6 high-traffic read tools (`server_info`, `server_heartbeat`, `workspace_status`, `workspace_list`, `workspace_health`, `workspace_drift_check`). Clients with MCP 2025-06-18 support get typed structured payloads; clients without continue to receive the existing text-channel JSON unchanged. Closes `tool-output-schema-batch-1-server-info-workspace`. |
| Backlog sync | Close rows: [`tool-output-schema-batch-1-server-info-workspace`]. Mark obsolete: []. Update related: future per-tool batches (symbols/navigation, validation, references/consumers, etc.) will be filed as Tier-1 follow-on rows after this batch ships. |

### 6. mcp-registry-publication

| Field | Content |
|---|---|
| Status | merged (PR #502, 2026-05-05) |
| Backlog rows closed | `mcp-registry-publication` |
| Diagnosis | Pure outreach + manifest work. No `src/` changes. Verified live: `.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json` exist; root `README.md` carries install snippets. The MCP Registry submission process must be re-confirmed at submission time — its API and listing format may have evolved since the roadmap entry was written. |
| Approach | (a) Confirm the MCP Registry's current submission process (web form / API / PR-to-registry). (b) Author a registry manifest entry from `plugin.json` + `marketplace.json` data. (c) Submit. (d) Once approved, add a registry-link / badge to root `README.md` and to `plugin.json` (if a registry-link field exists per current spec). |
| Scope | Production files: 3 — `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, root `README.md`. Within Rule 3 (3/4). Test files: 0 new (no testable surface). |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) Registry submission process may have changed since the roadmap entry — confirm at submission time. (2) Don't publish stale version metadata; the manifest must reflect the latest published NuGet version. (3) Don't break `plugin.json` schema when adding a registry field — validate against the current Claude Code plugin schema before committing. |
| Validation | (a) `git diff` review of the three files. (b) Submit through the registry process; capture the registry URL. (c) Manual: install the plugin from the registry on a clean Windows account, confirm parity with the marketplace install path. |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Maintenance: published the Roslyn MCP server to the public MCP Registry (alongside the existing GitHub plugin marketplace). Adds a registry badge + install snippet to the root `README.md`. Closes `mcp-registry-publication`. |
| Backlog sync | Close rows: [`mcp-registry-publication`]. Mark obsolete: []. Update related: []. |

### 7. apply-with-verify-diff-not-counts

| Field | Content |
|---|---|
| Status | merged (PR #504, 2026-05-05) |
| Backlog rows closed | `apply-with-verify-diff-not-counts` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` exists; `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` carries the diff helper used by `validate_recent_git_changes`. Today's verify step compares pre/post-apply error counts; ~14% of rollbacks (5/36 over 14 days) are false positives where a pre-existing diagnostic flipped severity class on the post-apply build path even though the apply itself was innocent. The diff helper already exists; reuse it. |
| Approach | (a) Switch the verify step in `ApplyWithVerifyTool` (or its underlying `IApplyWithVerifyService` impl) from count-delta to identity-diff: compare diagnostic identity (id + file + line) pre/post-apply rather than counts. (b) Reuse the diff helper in `ValidationBundleTools.cs`; no new diff logic. (c) Rollback only when the post-apply diagnostic set has *new* identities (not in pre-apply set) — pre-existing diagnostics whose severity flipped don't trigger rollback. (d) Confirm the underlying service impl in `src/RoslynMcp.Roslyn/Services/` is the right edit point (not the tool wrapper). |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` (tool wrapper; usually thin), `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` (diff helper to expose / reuse), and the underlying service impl in `src/RoslynMcp.Roslyn/Services/` (confirm at impl time — likely `ApplyWithVerifyService.cs`). Within Rule 3 (3/4). Test files: 2 — extend an existing test class with: (a) false-positive scenario (pre-existing diagnostic, innocent apply → MUST NOT roll back), (b) true-positive scenario (apply introduces new diagnostic → MUST roll back). |
| Tool policy | edit-only |
| Estimated context cost | 45000 |
| Risks | (1) The diff helper in `ValidationBundleTools` may be private/static — confirm reusability without escalating its visibility unnecessarily. (2) Diagnostic identity comparison must include id + file + line; not just id (multiple instances on different lines are common). (3) Don't break the existing rollback API surface — the response contract stays the same; only the trigger condition changes. (4) Test cases must control the pre-apply diagnostic state precisely; flaky severity may otherwise hide regressions. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New false-positive + true-positive tests in the existing test class. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: contrive a pre-existing diagnostic, apply an innocent edit, confirm no rollback. |
| Performance review | Hot-path adjacent — `apply_with_verify` is in the inner loop of refactor sessions. Diff vs count is the same order of cost (one set comparison per file). Spot-check P50 stays in budget. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: `apply_with_verify` no longer false-positive-rolls-back when a pre-existing diagnostic flips severity class on the post-apply build path. Verify step now compares diagnostic identity (id + file + line) pre/post-apply rather than counts; only newly-introduced diagnostics trigger rollback. Reduces observed false-positive rate from ~14% (5 of 36 rollbacks in the 14-day retro window) toward zero. Closes `apply-with-verify-diff-not-counts`. |
| Backlog sync | Close rows: [`apply-with-verify-diff-not-counts`]. Mark obsolete: []. Update related: []. |

### 8. elicit-workspace-path-on-missing-required-arg

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `elicit-workspace-path-on-missing-required-arg` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` is the filter that maps exceptions to the error envelope today; `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` carries the `workspace_load` tool. Retro §3 #2: 199 `InvalidArgument` matches across 25 of 40 sessions; the largest cluster is missing `path` on `workspace_load`. MCP `elicitation/create` (2025-06-18) — wrapped by the C# SDK as `McpServer.ElicitAsync` — provides a fallback path when the client declares the `elicitation` capability. |
| Approach | (a) In `StructuredCallToolFilter`, intercept `InvalidArgument: missing 'path'` from `workspace_load`. (b) Check the client's `elicitation` capability (via the `IMcpServer` instance the filter has access to; if not, plumb it). (c) If supported: call `ElicitAsync` with a strict path-only schema, retry the tool call with the elicited path, and return the success response. If declined or unsupported: return the existing `schemaHint`-augmented envelope (current behavior). (d) Strict scope: `workspace_load.path` only. Sensitive argument elicitation (credentials, tokens, secrets) is forbidden per MCP spec § Elicitation security; the filter MUST refuse to elicit anything outside the allowlist. |
| Scope | Production files: 2 — `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (elicit fallback + sensitive-arg refusal), `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (small surface-only edit if needed for the elicited path retry path; confirm at impl time — may not need to change). Within Rule 3 (2/4). **Tool-surface-only exemption candidate, but actually a middleware behavior change — Rule 3 applies normally.** Test files: 3 — extend existing or add new: (a) elicit-supported test (mock client returns "accept" with path), (b) fallback test (no elicitation capability → existing envelope), (c) sensitive-data-refused test (filter refuses to elicit a credential field). |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | (1) Sensitive-argument refusal MUST be tested explicitly — a regression here is a security finding. (2) The elicitation request must be canceled when the user declines; don't retry with empty input. (3) The retry path must respect the original tool's argument set — only `path` changes. (4) Elicitation latency is unbounded (waiting on user); the filter must NOT hold the workspace lock during the wait. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) 3 new/extended tests. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: from a client that supports elicitation, call `workspace_load()` with no path, accept the elicited path, confirm load succeeds. |
| Performance review | N/A — error path. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `workspace_load` invokes MCP `elicitation/create` (2025-06-18) when called without a `path` argument and the client declares the `elicitation` capability. Sensitive-argument elicitation is explicitly forbidden — only the `path` argument qualifies. Clients without elicitation support continue to receive the existing `schemaHint`-augmented `InvalidArgument` envelope. Closes `elicit-workspace-path-on-missing-required-arg`. |
| Backlog sync | Close rows: [`elicit-workspace-path-on-missing-required-arg`]. Mark obsolete: []. Update related: []. |

### 9. elicit-disambiguation-on-multi-symbol-resolve

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `elicit-disambiguation-on-multi-symbol-resolve` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` carries `symbol_search`, `find_references`, `go_to_definition` and siblings; `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` serializes the disambiguation list today. `StructuredCallToolFilter.cs` is where the capability-check helper would land (shared with initiative #8). Retro §2a row 4: 29 `NotFound: No symbol` envelopes after locator ambiguity in 17 of 40 sessions. **Rule 1 check vs initiative #8:** both reach into `StructuredCallToolFilter` for the capability-check helper, but the resolution paths are different (multi-symbol disambiguation vs missing-arg recovery), the regression tests don't share shape, and the symptom sets are disjoint. Don't bundle. |
| Approach | (a) Extract a shared `IMcpClientCapabilities.HasElicitation()` helper in `StructuredCallToolFilter.cs` (this lands first regardless of which initiative ships first). (b) In `SymbolTools.cs`, when a name resolves to multiple symbols (overloads, partial classes, inherited members), check elicitation capability. (c) If supported: call `ElicitAsync` with a select-from-N schema listing the candidates with descriptive labels (declaring type, signature, kind), retry the tool call with the elicited handle, return the success response. If declined or unsupported: return the existing disambiguation-list response (purely additive — no behavior change for incapable clients). |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (elicit-on-disambiguation path; primary site), `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` (label generation for select-from-N display), `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (extracted capability-check helper). Within Rule 3 (3/4). Test files: 2 — (a) elicit-supported test (mock client returns "accept" with handle index), (b) fallback test (no elicitation capability → existing list response unchanged). |
| Tool policy | edit-only |
| Estimated context cost | 50000 |
| Risks | (1) The select-from-N schema must produce labels that disambiguate candidates without leaking sensitive type names from internal-visibility code (don't leak unintended public surface). (2) The capability-check helper, if extracted in initiative #8 first, becomes a shared dependency; this initiative reuses it — coordinate at execute time on which lands first. (3) Multi-symbol resolution is touched by N tools (`symbol_search`, `find_references`, `go_to_definition`, possibly more); scope is `symbol_search` + `find_references` + `go_to_definition` only — sibling tools are deferred to follow-on rows if friction emerges. (4) Don't break existing disambiguation-list parsers in clients that don't support elicitation — fallback path must be byte-identical. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) 2 new tests. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: from a client that supports elicitation, call `symbol_search("Add")` against a project with multiple `Add` overloads, pick one, confirm the picked overload is returned. |
| Performance review | Elicitation latency is unbounded (user-paced). Don't hold workspace locks during the elicit wait. Single-symbol path (no disambiguation) is unchanged. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `symbol_search`, `find_references`, and `go_to_definition` invoke MCP `elicitation/create` when the resolved name is ambiguous (overloads, partial classes, inherited members) and the client declares the `elicitation` capability. The agent is asked to pick a candidate via a labeled select-from-N prompt; clients without elicitation continue to receive the existing disambiguation-list response. Closes `elicit-disambiguation-on-multi-symbol-resolve`. |
| Backlog sync | Close rows: [`elicit-disambiguation-on-multi-symbol-resolve`]. Mark obsolete: []. Update related: []. |

### 10. workspace-cache-prewarm-on-load

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `workspace-cache-prewarm-on-load` |
| Diagnosis | Verified live. `WorkspaceManager.cs` and `WorkspaceWarmService.cs` are the cited anchors; both exist. Initiative #4 (`workspace-load-uses-cache-fast-path`) makes the cache available; this initiative adds an opt-in `prewarm: true` flag that runs `workspace_warm` automatically and persists the warm artifacts (compilation hashes, per-project warm state) to the cache for the next load. OrchardCore profile: `workspace_warm` P95 = 17.32s, so persisting these makes a meaningful difference. |
| Approach | (a) Add a `prewarm: bool = false` parameter to the `workspace_load` tool (`WorkspaceTools.cs`) and thread through to `WorkspaceManager.LoadIntoSessionAsync`. (b) When `prewarm == true` and load succeeds, call `WorkspaceWarmService.WarmAsync` and capture the produced artifacts. (c) Persist the warm artifacts to the cache store via a new `IWorkspaceCacheStore.PutWarmArtifactsAsync(...)` method (extension to the contract from initiative #2; this requires the contract to be reopened — confirm at impl time whether the extension is additive or breaking). (d) On subsequent loads, the warm artifacts are seeded from cache. |
| Scope | Production files: 3 — `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (HOTSPOT — `LoadIntoSessionAsync` adds the post-load warm-and-persist step), `src/RoslynMcp.Roslyn/Services/WorkspaceWarmService.cs` (artifact-capture path), `src/RoslynMcp.Core/Services/IWorkspaceCacheStore.cs` (extend with warm-artifact methods if not already present from initiative #2). Within Rule 3 (3/4). Test files: 1 new — `tests/RoslynMcp.Tests/Workspace/WorkspaceCachePrewarmTests.cs` (load with `prewarm=true`, restart, verify warm artifacts present and reused on next load). |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | (1) `WorkspaceManager.cs` is an addenda hotspot — surgical addition of post-load step only. (2) The cache contract extension may require touching `WorkspaceCacheStore.cs` (impl) too; if so, that bumps the file count to 4. Plan tolerates 4. (3) Warm-artifact serialization shape must version-tag like the rest of the cache (initiative #2 sets the precedent). (4) Don't make `prewarm: true` the default in this initiative — see initiative #11 for the default-flip discussion. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New prewarm test. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: load OrchardCore with `prewarm=true`, restart server, load again, confirm warm-cache hit. |
| Performance review | Performance fix. Warm-cache `workspace_load + workspace_warm` should be ≤ 0.4 × cold-cache equivalent. Targets in `docs/large-solution-profiling-baseline.md` follow-up after merge. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: opt-in `prewarm: true` parameter on `workspace_load` — when set and the load succeeds, runs `workspace_warm` and persists the warm artifacts to the cache store. Subsequent loads of the same solution seed the warm artifacts from cache instead of recomputing. Defaults to `false` (no behavior change for callers that don't opt in). Closes `workspace-cache-prewarm-on-load`. |
| Backlog sync | Close rows: [`workspace-cache-prewarm-on-load`]. Mark obsolete: re-evaluate `workspace-warm-default-above-50-projects` after this lands — that row collapses to a 1-line default flip. Update related: []. |

### 11. workspace-warm-default-above-50-projects

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `workspace-warm-default-above-50-projects` |
| Diagnosis | Verified live. The row's own `do` text says "may be obsoleted by `workspace-cache-prewarm-on-load`". After initiative #10 ships, this row collapses to a one-line default flip: `prewarm: true` becomes the default when the loaded solution exceeds a project-count threshold. **Pre-flight at execute time:** if the default flip is genuinely 1 line, ship as a small initiative; if `prewarm`'s implementation makes the default-flip risky (cache-write contention on first load, cold-start regression on small solutions), mark `obsolete (subsumed by initiative #10)` and skip. |
| Approach | (a) After initiative #10 lands, set `prewarm: true` as the default in `workspace_load` when `LoadedSolution.ProjectCount > 50` and the caller did not pass `prewarm` explicitly. (b) Add `warm: false` opt-out semantics if not already provided by initiative #10 (single-flag opt-out is required by the row). |
| Scope | Production files: 2 — `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (HOTSPOT — default-evaluation logic), `src/RoslynMcp.Roslyn/Services/WorkspaceWarmService.cs` (only if a wired-in opt-out path is needed; confirm at impl time — may stay at 1 file). Within Rule 3 (2/4). Test files: 3 — (a) above-threshold test (warm fires automatically), (b) below-threshold test (warm doesn't fire), (c) explicit `warm: false` opt-out test. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | (1) Don't double-warm if a caller explicitly passes `prewarm: true` AND the solution is above threshold (idempotent semantics). (2) Threshold = 50 projects is the row's suggested floor; confirm OrchardCore profile still wins on this default and document the threshold in code. (3) `WorkspaceManager.cs` hotspot — surgical default-evaluation only. Scheduled non-adjacent to initiatives #4 and #10 which also touch it (positions 4, 10, 11 — see overall ordering). |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) 3 new tests. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: load a >50-project solution without `prewarm`, confirm warm fires; load a 5-project solution, confirm warm doesn't fire; explicit `warm: false`, confirm warm doesn't fire on a >50-project solution. |
| Performance review | This is the defaulted-on prewarm. Cold-start cost on first-time-loaded solutions adds a `workspace_warm` execution; warm-cache cost on subsequent loads is the cache-hit path from initiative #10. Acceptable per row's evidence. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Changed: `workspace_load` defaults `prewarm: true` for solutions with >50 projects. Cold-load callers on large solutions now automatically pay the warm-up cost up front so subsequent navigation/symbol calls hit warm caches. Override with `warm: false` to opt out. Closes `workspace-warm-default-above-50-projects`. |
| Backlog sync | Close rows: [`workspace-warm-default-above-50-projects`]. Mark obsolete: []. Update related: []. |

### 12. audit-deep-skill-create-and-mode-split

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | (split) `audit-deep-skill-migration` (paired with initiative #13) |
| Diagnosis | The original `audit-deep-skill-migration` row is bundle-shaped — covers (S3) skill structure migration + (B1–B5) skill bug fixes + (S2) /surface-audit wiring + (S5) archive-script. Per the planner's heroic-bundle pre-split rule, this is split. This initiative covers the structural migration: create the new `skills/audit-deep/` directory with `SKILL.md` (B1–B5 fixes applied) and the 3 mode-prompt variants (`prompts/full.md`, `prompts/promotion-only.md`, `prompts/read-only.md`). Initiative #13 covers (S2) + (S5). |
| Approach | (a) Create `skills/audit-deep/SKILL.md` with the new skill frontmatter (B6 description applied — already in the user-global slash-command form; this brings it into the plugin) and B1–B5 in the body: (B1) read-only against audited repo's main; Phase 6 mutations only in disposable worktrees the prompt creates; (B2) `mode = full|promotion-only|read-only` only; drop `focused`; (B3) prune the `no-holds-barred-audit.md` resolution branch; (B4/B5) require `mcp__roslyn__server_info` and halt instead of running a generic non-MCP fallback. (b) Move the 872-line prompt content to `skills/audit-deep/prompts/full.md`, leaving `ai_docs/prompts/deep-review-and-refactor.md` as a thin pointer to the skill location (or deleting it if the next pass shows zero references). (c) Create `skills/audit-deep/prompts/promotion-only.md` (thin: references full.md but pre-sets mode=promotion-only and skips Phase 6 in the prompt header). (d) Create `skills/audit-deep/prompts/read-only.md` (thin: references full.md but pre-sets mode=read-only and skips all mutation phases plus the promotion scorecard). |
| Scope | Production files: 4 — `skills/audit-deep/SKILL.md` (new), `skills/audit-deep/prompts/full.md` (new — content moved from `ai_docs/prompts/deep-review-and-refactor.md`), `skills/audit-deep/prompts/promotion-only.md` (new), `skills/audit-deep/prompts/read-only.md` (new). **Rule 3 structural-unit exemption: 4 units (skill + 3 mode prompts) — addenda's structural-unit shape applied to a new skill rather than a new tool.** The deprecation note in `~/.claude/commands/audit-deep.md` is consumer-side / user-global and not in this repo's file count. The deletion of `ai_docs/prompts/deep-review-and-refactor.md` (if executed) replaces a 4th touch with a deletion — same file count. Test files: 1 new — `tests/RoslynMcp.Tests/Skills/AuditDeepSkillFrontmatterTests.cs` verifying SKILL.md frontmatter parity + tool-reference validity against the live catalog (matches Phase 16b's per-skill checks). |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) Moving the 872-line prompt is mechanical, but B1–B5 application requires precise edits at known sections — read the prompt twice before edit. (2) The mode-specific prompt files must NOT duplicate the full.md content; they should be thin references with mode-specific overrides. (3) `tests/RoslynMcp.Tests/Skills/` may not exist yet — first-test-file scaffold may be needed (1 file extra, accept). (4) The `skills/audit-deep/` directory is shipped to plugin consumers — this becomes an installable consumer-facing skill at `/roslyn-mcp:audit-deep` after the next release. Plan documentation impact (CHANGELOG) accordingly. |
| Validation | (a) `mcp__roslyn__compile_check` per edit (where `mcp__roslyn__compile_check` doesn't apply to markdown — use `./eng/verify-ai-docs.ps1` instead). (b) New `AuditDeepSkillFrontmatterTests` covering frontmatter + tool-reference validity. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: invoke `/roslyn-mcp:audit-deep` (post-release) and confirm Phase -1 server-precondition hard gate fires when MCP server is absent. |
| Performance review | N/A. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `/roslyn-mcp:audit-deep` plugin skill — the comprehensive Roslyn MCP server audit + experimental-promotion scorecard + plugin-skill audit, now shipped with the plugin instead of relying on each consuming repo to keep `ai_docs/prompts/deep-review-and-refactor.md` current. Three modes: `full`, `promotion-only`, `read-only`. Skill requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts with a clear message when absent rather than running a non-MCP fallback. Closes `audit-deep-skill-migration` (paired with the archive-and-surface-audit-integration follow-on). |
| Backlog sync | Close rows: [`audit-deep-skill-migration`] (paired with initiative #13's CHANGELOG entry — same row, two follow-on commits). Mark obsolete: []. Update related: `audit-deep-subagent-orchestration` becomes runnable. |

### 13. audit-deep-archive-and-surface-audit-integration

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | (split) `audit-deep-skill-migration` (paired with initiative #12) |
| Diagnosis | The (S2) and (S5) pieces of the original heroic `audit-deep-skill-migration` row. (S2) wires Phase 0's drift-detection step (added in PR #494) to delegate to `/surface-audit` when available, instead of re-walking the catalog from scratch. (S5) adds an archive script that moves `ai_docs/audit-reports/*.md` older than N days into `archive/<YYYY>/`. Both are post-migration follow-ups that require the new skill structure to be in place. |
| Approach | (a) Add a wrapper at `skills/audit-deep/scripts/archive-old-reports.ps1` that takes `-OlderThanDays` (default: 30) and `-DryRun` flags. The script uses `Get-ChildItem` over `ai_docs/audit-reports/*.md` and `Move-Item` into `ai_docs/audit-reports/archive/<YYYY>/`. (b) In `skills/audit-deep/SKILL.md`, add a Phase 0 step: when `/surface-audit` is available in the host, delegate the drift-detection sub-step to it (one structured table back) instead of re-walking the live catalog. (c) Document the archive script in the SKILL.md "operational notes" section with usage examples. |
| Scope | Production files: 2 — `skills/audit-deep/scripts/archive-old-reports.ps1` (new), `skills/audit-deep/SKILL.md` (Phase 0 surface-audit delegation + archive-script doc paragraph). Within Rule 3 (2/4). Test files: 1 new — `tests/RoslynMcp.Tests/Skills/ArchiveOldReportsScriptTests.cs` (PowerShell-script invocation in dry-run mode against a synthetic file set). |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | (1) PowerShell script must work on Windows and be invocable via `pwsh -NoProfile -File ...` from Bash — confirm cross-shell compatibility. (2) Archive-script must be idempotent — running it twice doesn't break or duplicate. (3) `/surface-audit` delegation must fall through cleanly when the skill is absent (not all hosts have all skills installed); the prompt's drift-detection still runs the in-prompt logic as fallback. |
| Validation | (a) `./eng/verify-ai-docs.ps1`. (b) New script test. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: run `archive-old-reports.ps1 -DryRun` against a real `ai_docs/audit-reports/` dir, verify the move-list is correct without writing. |
| Performance review | N/A. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `/roslyn-mcp:audit-deep` Phase 0 delegates drift detection to `/surface-audit` when available, and ships an archive script (`skills/audit-deep/scripts/archive-old-reports.ps1`) that moves `ai_docs/audit-reports/*.md` older than 30 days into `archive/<YYYY>/`. Closes the (S2) + (S5) follow-on pieces of `audit-deep-skill-migration`. |
| Backlog sync | Close rows: [`audit-deep-skill-migration`] (the same row closed by initiative #12; this initiative's PR cites both initiatives). Mark obsolete: []. Update related: []. |

### 14. audit-deep-subagent-orchestration

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `audit-deep-subagent-orchestration` |
| Diagnosis | Per the row, the `/audit-deep` `mode=full` run can take 90–180 min and consume large chunks of the orchestrator's context window for raw tool output. The prompt's principle #1 already says "delegate long-running/log-heavy validation to subagents" but the offload pattern isn't formalized. Phase 6 (refactoring) and the preview/apply chains MUST stay inline (workspace-version-sensitive per principle #3). Phases 1, 2, 8, 8b are the offload candidates. |
| Approach | (a) Create `.claude/agents/audit-phase-runner.md` with the subagent definition: takes a phase number + repo context, runs the phase's tool calls, returns a structured-summary message (pass/fail counts, failing test names, duration, anomalies — never raw logs). (b) Update `skills/audit-deep/SKILL.md` (post-migration) with orchestrator logic that spawns the subagent for Phases 1, 2, 8, 8b and inlines Phases -1, 0, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 16b, 17, 18. (c) Define the structured-summary message contract (markdown table format the orchestrator parses). (d) Per the addenda's hooks-block-subagents caveat: the subagent's `toolPolicy` is `edit-only` (no `*_apply` calls in audit phases). |
| Scope | Production files: 2 — `.claude/agents/audit-phase-runner.md` (new), `skills/audit-deep/SKILL.md` (orchestration logic — post-migration; depends on initiative #12). Within Rule 3 (2/4). Test files: 1 new — `tests/RoslynMcp.Tests/Skills/AuditPhaseRunnerHandoffTests.cs` (against a small fixture solution, exercises the orchestrator-subagent handoff for a single phase, asserts the orchestrator receives a structured-summary message back, not raw tool output). |
| Tool policy | edit-only |
| Estimated context cost | 50000 |
| Risks | (1) Subagent context isolation: the subagent doesn't see prior orchestrator turns, so its `toolPolicy` MUST be edit-only (per addenda's cold-context preview-evidence caveat) and its prompt must be self-contained. (2) Structured-summary message contract is the wire format between orchestrator and subagent — define it precisely; format drift is a P2 finding. (3) Test fixture solution must be small enough to run cheaply but large enough to exercise the offload (e.g. 3 projects with diagnostics). (4) Phase 6 stays inline — confirm the SKILL.md orchestration explicitly excludes Phase 6 from delegation. |
| Validation | (a) `./eng/verify-ai-docs.ps1`. (b) Phase-runner handoff test. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: run `/roslyn-mcp:audit-deep mode=read-only` against a fixture solution, observe the subagent spawns for Phases 1, 2, 8 and the orchestrator only sees structured summaries. |
| Performance review | N/A — observability change. Throughput improvement is the point but isn't gated by a perf threshold here. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `/roslyn-mcp:audit-deep` offloads Phases 1, 2, 8, and 8b to a new `audit-phase-runner` subagent. The orchestrator receives structured-summary messages instead of raw tool output, materially reducing main-agent context consumption on a `mode=full` run. Phase 6 (refactoring) and preview/apply chains continue to run inline (workspace-version-sensitive). Closes `audit-deep-subagent-orchestration`. |
| Backlog sync | Close rows: [`audit-deep-subagent-orchestration`]. Mark obsolete: []. Update related: []. |

### 15. promote-tier-skill

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `promote-tier-skill` |
| Diagnosis | Verified live. `[McpToolMetadata]` annotations live next to `[McpServerTool]` methods; the tier literal is parameter index 1 of the attribute. `ServerSurfaceCatalog.<Category>.cs` partials carry the matching catalog entry; `SurfaceCatalogTests` enforces parity. `/publish-preflight` Step 8 emits a manual `Edit: ...` checklist for each `recommendation: "promote"` row; this skill replaces that with one tool call. |
| Approach | (a) Create `.claude/skills/promote-tier/SKILL.md` (maintainer-side; this is an internal release-cut helper, NOT a consumer-facing plugin skill). (b) Skill input: tool/resource/prompt name + target tier (`stable` or `experimental`). (c) Use `mcp__roslyn__symbol_search` to find the method by name; locate the `[McpToolMetadata]` attribute via `find_references` to the attribute type; edit the literal at parameter index 1. (d) Locate the matching `ServerSurfaceCatalog.<Category>.cs` entry by tool name; edit the tier literal. (e) For resources: edit `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`. For prompts: edit `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.*.cs`. (f) Run `dotnet test --filter SurfaceCatalogTests` to confirm parity post-edit. |
| Scope | Production files: 1 — `.claude/skills/promote-tier/SKILL.md` (new; maintainer-only). Test files: 1 new — `tests/RoslynMcp.Tests/Skills/PromoteTierRoundTripTests.cs` (round-trip: promote a known experimental tool to stable, build to confirm `SurfaceCatalogTests` parity passes, then revert via the inverse call and reconfirm). Within Rule 3 (1/4) and Rule 4 (1/3). |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) The tier-flip edit must be exact-string — wrong attribute parameter index or matching the wrong literal in a partial-class file produces a parity test failure but the skill should fail-loudly with the expected catalog row vs. observed. (2) Round-trip test must restore the original tier — leaving a tool in the wrong tier post-test pollutes the catalog. (3) Resource/prompt files have different attribute shapes than tool methods; the skill needs three resolver paths, one per kind. |
| Validation | (a) `mcp__roslyn__compile_check` after the test runs. (b) `PromoteTierRoundTripTests` covering tool, resource, and prompt promotion paths. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: invoke the skill against a known experimental tool, run `SurfaceCatalogTests`, confirm parity. |
| Performance review | N/A — release-time helper. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Maintenance: added `.claude/skills/promote-tier/` (maintainer-side) — automates the `[McpToolMetadata]` + `ServerSurfaceCatalog` tier flip the `/publish-preflight` Step 8 gate currently surfaces as a manual `Edit:` checklist. Pieces A and B of the release-cut promotion gate landed in PR #496; this is piece C. Closes `promote-tier-skill`. |
| Backlog sync | Close rows: [`promote-tier-skill`]. Mark obsolete: []. Update related: []. |

### 16. sampling-driven-tool-flows-spike

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `sampling-driven-tool-flows-spike` |
| Diagnosis | Investigation row, not an implementation row. The spike's deliverable is a one-page note (`ai_docs/items/sampling-spike.md`) with go/no-go per candidate; per-candidate follow-up rows are filed only on go. The row marks itself **weaker evidence — N until at least one candidate shows clear value**. The spike is bounded — half-day, no production code beyond the note. |
| Approach | (a) Read the C# SDK's `McpServerExtensions.SampleAsync` surface and document the call shape. (b) For each candidate (auto-XML-doc generation, refactor-summary text from `workspace_changes`, auto-test-name from method-name + test-class context), prototype a sample call against a fixture and capture (i) cost, (ii) latency, (iii) quality vs the agent's outer-loop equivalent. (c) Write `ai_docs/items/sampling-spike.md` with go/no-go + brief rationale per candidate. (d) File per-candidate follow-up backlog rows for each `go`; do not enable sampling on any tool in this PR. |
| Scope | Production files: 1 — `ai_docs/items/sampling-spike.md` (new note). No `src/` changes. Test files: 0 — investigation only. Within Rule 3 (1/4). |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) Spike scope must stay bounded — if the prototype reveals deep wiring is needed for *any* sample call (e.g. session-scoped `IMcpServer` plumbing), defer to follow-on rows rather than do the wiring in the spike. (2) Don't produce false-positive "go" for candidates whose context advantage is marginal — the row's evidence bar is "clear value" not "could maybe work". |
| Validation | (a) Note exists at `ai_docs/items/sampling-spike.md` and parses as valid markdown. (b) Each candidate has a go/no-go verdict + rationale + cost/latency numbers. (c) Per-candidate follow-up rows filed on go. |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Maintenance: spike note `ai_docs/items/sampling-spike.md` evaluated server-initiated MCP `sampling/createMessage` against three candidate tool flows. Per-candidate follow-up rows filed for the `go` verdicts; no tool body changes in this PR. Closes `sampling-driven-tool-flows-spike`. |
| Backlog sync | Close rows: [`sampling-driven-tool-flows-spike`]. Mark obsolete: []. Update related: per-candidate follow-up rows filed at execute time per spike outcome. |

### 17. (skipped) tool-surface-pagination-or-tool-sets

The Low row `tool-surface-pagination-or-tool-sets` is **TRACK-only** per its own do-cell language. Trip conditions: (1) a small-model client reports tool-discovery friction OR (2) surface count crosses ~200 tools (live count today: 167, per `mcp__roslyn__server_info`). Neither has fired. Excluded from this plan; the next sweep will re-evaluate after the Tier-1 schema batches land.

## Skipped rows

| Row | Reason |
|---|---|
| `tool-surface-pagination-or-tool-sets` | Track-only per row text; trip conditions not met (167/200 tools, no external small-model friction reported). |
| `validate-locator-preflight-tool` (Defer) | Deferred per 7-day re-measurement window after `inv-arg-envelope-schema-hint` (re-evaluate after 2026-05-12). |
| `workspace-process-pool-or-daemon` (Defer) | Deferred per future-worse-profile-evidence gate. OrchardCore profile didn't justify daemon. |
| `http-streamable-host-project` (Defer) | Deferred pending concrete remote-deployment driver (named users + auth/observability/tenancy plan). |

## Self-vet checklist

- [x] **Rule 1.** `audit-deep-skill-migration` is the only bundle and is **split** into initiatives #12 + #13 per the heroic-bundle pre-split rule (the row's `do` text contains S3 + B1–B5 + S2 + S5 — distinct concerns, not single code path). The split is cited in both initiatives' Diagnosis fields.
- [x] **Rule 3.** No initiative touches more than 4 production files. Initiatives #2 and #12 invoke the structural-unit exemption (3 units in #2, 4 units in #12) with explicit citation in Scope. Mandatory addenda (`TestBase.cs`) counted in #2's file budget.
- [x] **Rule 3b.** Every initiative has `toolPolicy = "edit-only"` — no preview-then-apply work needed across the batch (all initiatives are new code, attribute additions, middleware behavior changes, doc moves, or investigation outputs).
- [x] **Rule 4.** Max 3 new test files per initiative observed (#10 hits 3; others ≤ 2).
- [x] **Rule 5.** All initiatives ≤ 60K tokens (ceiling 80K). #5 at 60K is the heaviest (6-tool batch attribute updates). No `heroic-last` flags applied — #14 is dependent on #12 but isn't heroic.
- [x] **Hotspot distribution.** `ServerSurfaceCatalog.*.cs` partials touched by initiatives #3 and #5 — order positions 3 and 5, non-adjacent. `WorkspaceManager.cs` touched by #4, #10, #11 — order positions 4, 10, 11; non-adjacent. `ServiceCollectionExtensions.cs` touched by #2 — only one initiative.
- [x] **Markdown link hrefs.** Plan uses plain inline-code source citations throughout — no markdown-link form pointing at the `src/` tree, which would resolve to non-existent paths under `ai_docs/plans/<ts>_backlog-sweep/`. Verified safe against `verify-ai-docs.ps1`.
