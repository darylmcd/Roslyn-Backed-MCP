# server-guidance-callable-tools-autoreload — name callable tools and the auto-reload default in server guidance

**row:** `server-guidance-callable-tools-autoreload` · **pri:** `Medium` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkflowRecommendationTools.cs`
- `src/RoslynMcp.Host.Stdio/ServerInstructions.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `tests/RoslynMcp.Tests/WorkflowRecommendationToolsTests.cs`

## Acceptance

- [ ] Both non-callable payload names are gone from `WorkflowRecommendationTools.cs`: the no-match branch (`PrimaryTools: ["discover_capabilities"]`, :106) names only registered tools — e.g. `symbol_search`, `find_references`, `semantic_grep`, `get_source_text`, `compile_check` — and the related-tests branch (`FollowUpTools: ["test_run --filter", …]`, :79) uses the bare `test_run`, with the filter hint moved into `Why`. `discover_capabilities` appears in no `PrimaryTools`/`FollowUpTools` list, since it is registered as a prompt (`RoslynPrompts.AnalysisWorkflows.cs:90`) reachable only via `prompts/get` or the `get_prompt_text` shim
- [ ] A regression in `WorkflowRecommendationToolsTests.cs` (56 lines today, no payload-name coverage) asserts every name emitted in `PrimaryTools`/`FollowUpTools` across all branches resolves via `ServerSurfaceCatalog.TryGetTool`; the only exemption is an explicit pinned allowlist of `roslyn://`-scheme resource URIs (today just `roslyn://server/catalog`), so both a new non-tool name and a new unpinned resource URI fail the build
- [ ] `ServerInstructions.Text` (experimental profile) names `semantic_grep` on the navigation-and-analysis line. `StableOnlyText` is left unchanged ONLY while `semantic_grep` is still experimental-tier (`ServerSurfaceCatalog.Analysis.cs:53`); if `promotion-tier-analysis-batch-1` has promoted it by implementation time, name it in `StableOnlyText` as well
- [ ] `ServerInstructions.Text` adds one sentence: workspace-scoped calls auto-reload a stale workspace by default (`OnStale = StalenessPolicy.AutoReload`, `ExecutionGateOptions.cs:59`), so no pre-emptive `workspace_reload` is needed. The sentence MUST NOT direct agents to watch for `staleAction: "warn"`: that value is stamped only under a non-default `ROSLYNMCP_ON_STALE=warn` host (`WorkspaceExecutionGate.cs:459-465`) and unset `_meta` observability fields are now omitted entirely (`changelog.d/meta-omit-null-fields.md`, #1421). Permitted signal wording is limited to what the default profile can actually emit — `staleAction: "auto-reloaded"` after a reload, and `restoreRequired: true` on workspace load/reload/status responses
- [ ] Both `Text` and `StableOnlyText` stay within `ServerInstructions.ClientCharacterLimit` (2048); `Text` measures 1,149 chars today, so both additions fit without trimming. `ServerDiscoveryWireTests` (:182-185, equality against the constant plus the limit) and `StartupDiagnosticsTests` (:400-424, limit only) stay green with no edit
- [ ] The `workspace_reload` `[Description]` (`WorkspaceTools.cs:103`) is REWRITTEN — not extended — to lead with the auto-reload default instead of presenting reload as the way to pick up file changes. It stays ≤ 250 chars (184 today, 66 headroom) and the swept slice total stays ≤ 4,600 (3,902 today) so `ToolDescriptionDietWorkspaceValidationTests` stays green; any detail that does not fit goes into the existing XML `<remarks>` block above the method, per `ToolDescriptionBudgetHarness`'s own failure guidance
- [ ] Guidance/payload text only — no change to routing behavior, staleness policy, gate semantics, or wire shape

## Evidence

- The router's no-match branch returns `discover_capabilities` as a primary tool, but it is a prompt, so following it as a tool dead-ends (deterministic across 5 codex sessions); an independent sweep of the payload found a second non-callable entry, `test_run --filter` (:79). Agents also reload defensively before every check (117 codex / 4 claude sessions) despite the server already auto-reloading and stamping `staleAction: "auto-reloaded"`, and route C# token searches to `grep`/`rg` because `semantic_grep` is not named in the bootstrap (196 codex / 23 claude discovery lookups). One reload held the gate `heldMs: 10345`.
- `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — finding `server-guidance-callable-tools-autoreload` (§4.7); 2a#recommend_workflow-generic-discover-capabilities-fallback, 2a#workspace_reload-gate-hold-latency, 2b#token-search-semantic_grep, 3#defensive-reload-after-out-of-band-edit.

## Context

Related-but-distinct live rows, verified non-overlapping: `tool-surface-pagination-or-tool-sets` (Low) adds catalog RESOURCES, not guidance text; `initiative-executor-roslyn-tool-discovery-experiment` (Low) is a client-side agent-brief measurement; `param-dedupe-deadcode-fixall-workflow-editorconfig` and `param-dedupe-workspace-validation-scaffolding` touch the same two files but only their PARAMETER `[Description]`s, not the payload constants or method descriptions. `promotion-tier-analysis-batch-1` (Medium, blocked) can flip the tier assumption in acceptance criterion 3 — check tier before writing `StableOnlyText`.
