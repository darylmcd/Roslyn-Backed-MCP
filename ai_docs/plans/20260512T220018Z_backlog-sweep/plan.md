# Backlog sweep plan — 20260512T220018Z

**Generated:** 2026-05-12T22:00:18Z
**Backlog snapshot:** 2026-05-12T21:20:00Z
**Initiative count:** 5
**Anchor verification:** performed (all initiatives ≤ 30K tokens or judgment-heavy — anchors probed by deepeners)

## Selection rationale

All Critical, High, and Medium sections are empty. 7 of 13 Low rows are Reserved (contributor-pickup) and excluded. 1 row (`tool-surface-pagination-or-tool-sets`) is explicitly "Track only; not yet actionable" (neither trigger condition met — surface count 167, below 200 threshold; no small-model friction report) and excluded. All 5 Defer rows are excluded.

**Skipped rows:**

- `add-project-reference-self-reference-not-rejected` — Reserved gh #608
- `compile-check-project-filter-miss-no-error-envelope` — Reserved gh #609
- `find-duplicated-methods-mcp-wrapper-false-positive` — Reserved gh #612
- `get-prompt-text-multi-step-required-param-errors` — Reserved gh #610
- `goto-type-definition-builtins-invalidoperation` — Reserved gh #607
- `test-run-unfiltered-no-failure-envelope` — Reserved gh #611
- `workspace-load-prewarm-double-nullable` — Reserved gh #606
- `tool-surface-pagination-or-tool-sets` — "Track only" self-exclusion (neither activation criterion met)
- Defer section (5 rows): `scorecard-blocked-to-backlog-row`, `validate-locator-preflight-tool`, `http-streamable-host-project`, `workspace-process-pool-or-daemon`, `workspace-manager-cache-store-extraction`

**5 rows selected** (all Low priority — no P2/P3 open rows exist):

| Order | Row id | Priority | Context cost | Schedule |
|---|---|---|---|---|
| 1 | `compile-check-not-connected-raw-transport-error-envelope` | correctness | 25K | normal |
| 2 | `list-analyzers-totalrules-variance` | UX-correctness | 28K | normal |
| 3 | `workspace-staleness-cross-workspace-contamination` | performance | 28K | normal |
| 4 | `skill-namespace-installed-as-bulk-frontmatter-migration` | maintenance | 15K | normal |
| 5 | `scaffolding-service-split-by-scaffold-type` | maintenance | 55K | heroic-last |

**Rule 1 bundle candidates:** none — no rows share the same code path.

---

## Initiatives (in order)

### 1. compile-check-not-connected-raw-transport-error-envelope

| Field | Content |
|---|---|
| Status | merged (PR #711, 2026-05-12) |
| Backlog rows closed | `compile-check-not-connected-raw-transport-error-envelope` |
| Diagnosis | Anchor at `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:19` is intact — the `[McpServerTool]` attribute at line 19 matches the cited location. The `StructuredCallToolFilter` at `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:115–154` wraps every `tools/call` dispatch in a try/catch and routes all exceptions through `ToolErrorHandler.ClassifyAndFormat`. The current handler dictionary in `ToolErrorHandler.cs:21–69` covers `StaleWorkspaceTransitionException`, `WorkspaceEvictedException`, `FileNotFoundException`, `DirectoryNotFoundException`, `KeyNotFoundException`, `ArgumentException`, `TimeoutException`, and `InvalidOperationException` — but has no entry for transport-level disconnects. The retro evidence (session 4d410565, subagent a843af1d, L131) shows `compile_check` returning the raw string `"Not connected"` with no `error: true` envelope. Architectural investigation confirms there are two plausible origins: (a) `InvalidOperationException("Not connected")` thrown by the MCP SDK transport pipe when a write is attempted on a disconnected `PipeStream` — this WOULD reach the filter's catch block but falls through to `InternalError` today (which DOES produce a structured envelope, so this path is probably not the one that produced the raw string); or (b) the exception occurs at the SDK's protocol-write layer AFTER `StructuredCallToolFilter` returns its `CallToolResult`, in which case it is a transport-layer signal outside app control and cannot be intercepted in the filter. The raw-string shape (no envelope at all, not even `InternalError`) points toward path (b), meaning the fix cannot be in `ToolErrorHandler.ClassifyError`. The backlog row's proposed location (`StructuredCallToolFilter` transport path) is the right search area, but the actual hook may need to be an SDK-level `OnTransportError` callback or a `try/catch` wrapping the entire `RunAsync` call in `Program.cs`. This architectural ambiguity, combined with one-shot evidence, is the primary risk. The weaker-evidence flag in the backlog row is well-placed. |
| Approach | 1. **Investigation phase (executor's first act):** instrument `Program.cs` to confirm whether a disconnected-pipe write exception propagates above the `host.RunAsync()` boundary or is swallowed by the SDK. Check the MCP SDK's `WithStdioServerTransport` handler for `InvalidOperationException` / `IOException` with message "Not connected". 2. **If path (a) — exception reaches filter:** add an `IOException`/`InvalidOperationException("Not connected")` entry to `ToolErrorHandler`'s `ErrorHandlers` dictionary in `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` at the top of the handler list (before `InvalidOperationException`), emitting `category: "Disconnected"` with `recovery: workspace_reload(workspaceId: "...")`. The `workspaceId` to embed in the hint is already in scope via `ToolDispatch.ReadByWorkspaceIdAsync` closure — surface it through an ambient context similar to `AmbientGateMetrics`. 3. **If path (b) — transport write fails after filter returns:** wrap the SDK's stdio transport in a try/catch in `Program.cs` `AppDomain.CurrentDomain.ProcessExit` handler, or add a `WithTransportErrorHandler` hook if the SDK exposes one, to log the raw "Not connected" event and attempt a graceful-eviction record (so the NEXT `compile_check` call from a retried `workspaceId` receives `WorkspaceEvicted` rather than `NotFound`). The fix does NOT touch `CompileCheckTools.cs` directly — the shim correctly delegates to `ToolDispatch.ReadByWorkspaceIdAsync`; the gap is in the error classification layer below or above the filter. Files to modify: `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` (add handler) and/or `src/RoslynMcp.Host.Stdio/Program.cs` (transport-error wrap). The `WorkspaceEvicted` shape in `ToolErrorHandler.cs:34–50` is the pattern to mirror for the recovery hint text. |
| Scope | Production files: 2 — `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`, `src/RoslynMcp.Host.Stdio/Program.cs` (one or both depending on investigation path). Test files: 1 — extend `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs` with 1 new test method simulating an `InvalidOperationException("Not connected")` via `BuildErrorResult` and asserting a structured envelope with `category: "Disconnected"`. Rule 3 exemption: tool-surface-only, 2 files (the fix is an error-envelope / response-shape change on an already-registered tool, no new tool surface). |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | (1) Transport path ambiguity: if the raw "Not connected" string is a protocol-layer write failure AFTER the filter returns, adding a handler to `ToolErrorHandler` will not intercept it. The executor must confirm the exception origin before committing to approach. (2) One-shot evidence: the backlog row's own weak-evidence flag applies — this may be a race condition in the MCP SDK's stdio transport that only manifests on the first `tools/call` after a host-process recycle, making it hard to reproduce deterministically in a unit test. (3) SDK coupling: `InvalidOperationException("Not connected")` is an undocumented exception from `System.IO.Pipes.PipeStream` — the message string is not a stable API. The handler should match on message substring (`Contains("Not connected", OrdinalIgnoreCase)`) rather than equality. (4) InternalError overlap: if path (a) is correct, today's filter actually DOES produce a structured envelope (`InternalError` category) for any unhandled `InvalidOperationException` — meaning the raw-string report from the retro may represent path (b). Cross-check the v1.33.x changelog to confirm whether `StructuredCallToolFilter` was already present during session 4d410565 (2026-04-28). Fanout probe skipped — surgical edit to `ToolErrorHandler`, no cross-cutting symbol changes. |
| Validation | 1. `StructuredCallToolFilterTests`: add `BuildErrorResult_NotConnectedInvalidOperationException_EmitsDisconnectedEnvelope` — construct `new InvalidOperationException("Not connected")`, call `BuildErrorResult("compile_check", ex)`, assert `IsError=true`, `category="Disconnected"`, and `message` contains `workspace_reload`. 2. Confirm `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` passes. 3. Run `mcp__roslyn__test_run --filter "StructuredCallToolFilterTests"`. 4. Manual note: executor should check CHANGELOG.md for when `StructuredCallToolFilter` landed relative to 2026-04-28 to determine whether path (b) is the true root cause. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `compile_check` transport-disconnect errors now emit a structured `category: "Disconnected"` envelope with a `workspace_reload` recovery hint instead of a raw `"Not connected"` string. |
| Backlog sync | Close rows: [`compile-check-not-connected-raw-transport-error-envelope`]. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**
- **Pattern coordinates:** register in `ErrorHandlers` dictionary at `ToolErrorHandler.cs:20-69`. Mirror `WorkspaceEvictedException` shape at `ToolErrorHandler.cs:34-50`. Insert BEFORE generic `InvalidOperationException` at line 63.
- **Test target:** add `BuildErrorResult_NotConnectedInvalidOperationException_EmitsDisconnectedEnvelope` to existing `StructuredCallToolFilterTests.cs` after line 129. Mirror `BuildErrorResult_UnrecognizedException_ClassifiesAsInternalError` at lines 117-129. Do NOT create a new test file.
- **Edge cases:** (1) exact-message match; (2) no collision with `Rate limit` branch at 63-68; (3) no collision with `ShouldSuggestReloadAfterInvalidOperation` at 351-361.
- **Negative space:** do NOT modify `TryClassifyBindingLike` at `ToolErrorHandler.cs:275-315`; do NOT touch `IsInvocationWrapper` at 337-346; if path (b), wrap only `host.RunAsync()` at `Program.cs:163`.

</details>

---

### 2. list-analyzers-totalrules-variance

| Field | Content |
|---|---|
| Status | merged (PR #708, 2026-05-12) |
| Backlog rows closed | `list-analyzers-totalrules-variance` |
| Diagnosis | Root cause is in `src/RoslynMcp.Roslyn/Services/AnalyzerInfoService.cs:43`. The assembly-deduplication guard `if (analyzersByAssembly.ContainsKey(assemblyName)) continue;` causes whichever project happens to enumerate an assembly first to "win" the rule enumeration for that assembly. The rule list is obtained via `analyzerRef.GetAnalyzers(compilation.Language)` — language-specific. If project iteration order varies between workspace sessions (MSBuildWorkspace project load order is not guaranteed stable across `GetCurrentSolution` calls when compilation state evolves), a later-session run can encounter the same assembly from a different project first and collect a different rule count. The 87-rule swing (495 → 408) matches this pattern: one session first encounters a mixed-language project context, the other encounters a C#-only context for the same analyzer assemblies. The early-exit at line 43 then freezes whichever count was seen first. Additionally, rules are later deduplicated by `DistinctBy(r => r.Id)` at line 84 only within each assembly's collected list — if the first-project context misses rules that a later-project context would have found, those rules are silently dropped. The `totalRules` field in `AnalyzerInfoTools.cs:32` is a sum over the (post-dedup) service result — it is NOT a paged subset, so the field name `totalRules` is accurate once the underlying service is made deterministic. No rename is required. The fix belongs entirely in the service's collection loop. |
| Approach | Modify `src/RoslynMcp.Roslyn/Services/AnalyzerInfoService.cs`: remove the early-exit `ContainsKey` guard and instead accumulate rules from all projects for each assembly, then deduplicate by rule ID across the union at the end. Concretely: change `analyzersByAssembly` to collect into a `Dictionary<string, HashSet<string>>` for seen rule IDs alongside the rule list, and for each project/assembly pair merge new rules in rather than skipping. The existing `DistinctBy(r => r.Id).OrderBy(r => r.Id)` at lines 84–85 already provides final deduplication — the fix is to not short-circuit before all projects contribute. No signature changes to `IAnalyzerInfoService`. No changes to `AnalyzerInfoTools.cs`. Add a regression test asserting that two back-to-back `ListAnalyzersAsync` calls on the same workspace return identical `totalRules` counts and identical rule ID sets. |
| Scope | Production files touched: 1 — `src/RoslynMcp.Roslyn/Services/AnalyzerInfoService.cs`. Test files added: 1 — new test class (e.g. `tests/RoslynMcp.Tests/Tools/AnalyzerInfoToolsTests.cs`). Rule 3 exemption: tool-surface-only shape applies (fix is inside the registered tool's backing service implementation; no new tool surface, no DI registration changes). 2-file cap satisfied. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) Project iteration order may be intentionally non-deterministic as a side-effect of MSBuildWorkspace's lazy compilation model — verify that merging across projects yields a superset, not a different set, to avoid inflating counts spuriously. (2) The `Display ?? FullPath ?? "Unknown"` assembly key may not be stable across sessions — if `Display` changes between warm and cold load, the deduplication would fail to merge. (3) Fanout probe skipped — surgical edit to service implementation only, no cross-cutting symbol changes, no callers of `IAnalyzerInfoService` are modified. |
| Validation | (1) Run two back-to-back `list_analyzers` calls in a test against the same workspace; assert `totalRules` is identical in both responses and the returned rule lists are identical (sorted). (2) Run `list_analyzers` against `SampleSolution.slnx` with and without `projectName` filter; assert `totalRules` for the unfiltered call is ≥ the filtered call's value. (3) `mcp__roslyn__compile_check` after the edit. (4) `dotnet test --filter AnalyzerInfo`. (5) `./eng/verify-release.ps1 -Configuration Release`. |
| Performance review | N/A — correctness fix, no hot-path changes. The loop change adds per-rule set-union work proportional to total analyzer rule count (typically < 1000), dwarfed by `GetCompilationAsync` latency already paid per project. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `list_analyzers` returning non-deterministic `totalRules` counts across sessions against the same workspace. The service-layer deduplication guard was exiting early on the first project to reference an analyzer assembly, discarding rules visible only from other projects' language contexts. The fix accumulates rules from all projects before deduplicating by rule ID, making the result session-stable. |
| Backlog sync | Close rows: [`list-analyzers-totalrules-variance`]. |

---

### 3. workspace-staleness-cross-workspace-contamination

| Field | Content |
|---|---|
| Status | merged (PR #709, 2026-05-12) |
| Backlog rows closed | `workspace-staleness-cross-workspace-contamination` |
| Diagnosis | Root cause is in `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` at `ShouldIgnorePath` (line 147–151). `Watch(workspaceId, workspacePath)` resolves the watch directory as `Path.GetDirectoryName(workspacePath)` (line 44), then creates a recursive `FileSystemWatcher` (`IncludeSubdirectories = true`) on that directory. When workspace A loads from `C:\Repo\Solution.slnx`, the watcher monitors `C:\Repo\` recursively. A worktree workspace B loaded from `C:\Repo\.worktrees\agent-xxx\Solution.slnx` lives inside that tree. Every on-disk write `apply_project_mutation` makes in the worktree fires `MarkStaleIfRelevant` against workspace A's `WatcherEntry`, flipping its `IsStale` flag and triggering a 20–40 s auto-reload on the next tool call. The staleness flag itself (`_fileWatcher.IsStale(workspaceId)` at `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:106`) is correctly keyed per-workspace-id — the contamination is purely in the per-watcher directory scope. The existing `ShouldIgnorePath` exclusion covers `obj/`, `bin/`, `.git/` but not `.worktrees/`. The backlog row's suggestion to investigate `WorkspaceExecutionGate` / `IWorkspaceManager.IsStale` is confirmed: both are per-workspace-id-clean; the bug is one layer deeper in `FileWatcherService`. Note: the backlog row flags weak evidence (single session, single delay cluster). The code analysis confirms the mechanism is structurally present and reproducible given a worktree-sharing-root setup; whether the observed delays were caused by this specific path is corroborated but not proven by a second independent session. |
| Approach | 1. In `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`, extend `ShouldIgnorePath` (line 147) to also return `true` for paths containing `\.worktrees\` (or `/.worktrees/`). This mirrors the existing `obj/`, `bin/`, `.git/` exclusions. 2. Verify the separator-agnostic pattern matches Windows (`\`) and POSIX (`/`) paths — use `fullPath.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)`. No changes to `WorkspaceExecutionGate.cs`, `WorkspaceManager.cs`, or `IFileWatcherService`. |
| Scope | Production files touched: 1 — `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`. Test files added: 1 — extend `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs` with a new cross-workspace isolation test method. No files deleted. Rule 3 satisfied (1 production file). Rule 4 satisfied (1 test file extended). |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | 1. The `.worktrees/` exclusion is path-convention-dependent: it only helps when the worktrees directory is literally named `.worktrees/` under the solution root (the repo's documented convention). A user placing worktrees elsewhere (arbitrary `git worktree add` target) would still experience contamination. Mitigation for a later row: store the per-workspace load path in the `WatcherEntry` and reject events whose `fullPath` starts with a sibling workspace's load-root prefix. Out of scope here. 2. The fix must not suppress genuine external-edit detection for files inside the watched directory that are NOT in a `.worktrees/` subtree. 3. Weaker evidence flag: if a second independent session reproduces the delay through a different mechanism, this fix may be insufficient. 4. `ShouldIgnorePath` has no existing test coverage of the `.worktrees/` case — add an assertion alongside the regression test. |
| Validation | 1. New test in `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`: load workspace A from a temp solution path; create a `.worktrees/agent-xxx/` subdirectory; write a `.cs` file inside `.worktrees/agent-xxx/`; assert workspace A's `IsStale` remains `false` (with the `WatcherFlushTimeoutMs = 2000` pattern already used in that file). 2. Existing `ExternalEditStalenessTests` must pass unchanged — a write to a `.cs` file OUTSIDE `.worktrees/` must still flip `isStale = true`. 3. `mcp__roslyn__compile_check` clean after the edit. 4. `./eng/verify-release.ps1 -Configuration Release` (or `dotnet test --filter ExternalEditStaleness`). |
| Performance review | N/A — correctness fix, no hot-path changes. `ShouldIgnorePath` is called on every watcher event; adding one `Contains` check to a static method is negligible. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed cross-workspace staleness contamination where writes to a worktree workspace (`.worktrees/`) under the same solution root triggered spurious stale-reload delays on the primary workspace. |
| Backlog sync | Close rows: [`workspace-staleness-cross-workspace-contamination`]. |

---

### 4. skill-namespace-installed-as-bulk-frontmatter-migration

| Field | Content |
|---|---|
| Status | merged (PR #710, 2026-05-12) — wave 1/12 |
| Backlog rows closed | `skill-namespace-installed-as-bulk-frontmatter-migration` (wave 1 of ~12; see Backlog sync) |
| Diagnosis | All 46 `SKILL.md` files across `skills/*/SKILL.md` (32 files) and `.claude/skills/*/SKILL.md` (14 files) are missing the `installed_as:` frontmatter key — confirmed by running `eng/list-skills.ps1` live: "46 skill(s) found. 46 missing `installed_as:`". The test gate `tests/RoslynMcp.Tests/Skills/SkillFrontmatterInstalledAsTests.cs:39` has `[Ignore("Pending bulk frontmatter migration…")]` and will remain ignored until all 46 are patched. Infrastructure (enumerator script + test) was shipped in PR #694. No docs-only Rule 3 exemption exists in the addenda; SKILL.md files count against the ≤ 4 production-file cap. Wave 1 covers the first 4 `.claude/skills/` files alphabetically. |
| Approach | Wave 1: add `installed_as: <bare-name>` frontmatter key to 4 `.claude/skills/*/SKILL.md` files: `backlog-intake`, `backlog-split`, `bump`, `close-backlog-rows`. For each file, read the existing `name:` frontmatter field and add `installed_as: <same-bare-name>` immediately after the `name:` line. The `.claude/skills/` skills are maintainer-only (not shipped to plugin consumers) so their `installed_as:` values use bare kebab-case (no `roslyn-mcp:` namespace prefix), matching the `name:` field value. Validate each edit with the regex `^[a-z][a-z0-9-]+$` before saving. Do NOT touch the test file in this wave — the `[Ignore]` stays until the final wave. Run `eng/list-skills.ps1` after the 4 edits to verify the 4 entries no longer show `[missing]`. |
| Scope | Production files (4): `.claude/skills/backlog-intake/SKILL.md`, `.claude/skills/backlog-split/SKILL.md`, `.claude/skills/bump/SKILL.md`, `.claude/skills/close-backlog-rows/SKILL.md`. Test files modified (0): none this wave. Files deleted: none. Note: these are markdown documentation files, but the addenda grants no doc-only Rule 3 exemption; they count against the ≤ 4 cap. Waves 2–12 are tracked as spin-off backlog rows (see Backlog sync). |
| Tool policy | edit-only |
| Estimated context cost | 15000 |
| Risks | (1) Namespace choice: `.claude/skills/` files are maintainer-only; bare names are correct. The shipped `skills/` files (waves 5–12) may warrant `roslyn-mcp:` prefix if the plugin routes by namespace — confirm the routing logic (see `eng/list-skills.ps1` intent) before waves 5–12. (2) Name collision: if two skills share the same `name:` value, bare `installed_as:` would collide; audit `name:` uniqueness before each wave. (3) The backlog row remains open until wave 12 completes; partial migration is visible via `[Ignore]` test — no CI regression risk during the multi-wave run. (4) Fanout probe skipped — surgical doc edit, no cross-cutting symbol changes. |
| Validation | (1) After editing each file, run `eng/list-skills.ps1` and confirm the patched 4 entries show a non-`[missing]` value in `installed_as` column. (2) Verify each value matches `^[a-z][a-z0-9-]+$` (bare, no namespace). (3) Run `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` to confirm no build breakage. (4) Run `dotnet test --filter SkillFrontmatterInstalledAsTests` — test remains `[Ignore]`-marked and should report skipped (not failed). |
| Performance review | N/A — doc-only change, no runtime behavior. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Add `installed_as:` frontmatter key (wave 1/12) to `.claude/skills/` SKILL.md files: `backlog-intake`, `backlog-split`, `bump`, `close-backlog-rows`. Spin-off from PR #694 (`skill-namespace-and-semantic-search-discoverability`). |
| Backlog sync | Close rows: [`skill-namespace-installed-as-bulk-frontmatter-migration`] ONLY after wave 12 completes. This wave (1/12): do not close the master row. At Step 7 sync, add spin-off backlog rows for waves 2–12, each scoped to 4 SKILL.md files; the final wave also removes `[Ignore]` from `tests/RoslynMcp.Tests/Skills/SkillFrontmatterInstalledAsTests.cs:39`. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**
- **Exact edit shape:** insert `installed_as: <bare-name>` immediately after `name:` line (between lines 2 and 3 of frontmatter).
- **Frontmatter parser contract:** `eng/list-skills.ps1:69-85` and `SkillFrontmatterInstalledAsTests.cs:117-142`.
- **Validation regex:** `^[a-z][a-z0-9-]+$`.
- **Verification command:** `pwsh -NoProfile -File eng/list-skills.ps1` — "46 missing" drops to "42 missing".
- **Negative space:** do NOT remove `[Ignore]` from `SkillFrontmatterInstalledAsTests.cs:39`; do NOT touch other 42 SKILL.md files.

</details>

---

### 5. scaffolding-service-split-by-scaffold-type

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `scaffolding-service-split-by-scaffold-type` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` is a sealed class currently at 2776 lines (the backlog row cited 2521 — the file has grown ~10% since the row was written; the stale LOC count does not affect the substance). It implements `IScaffoldingService` with four public entry points: `PreviewScaffoldTypeAsync` (line 38), `PreviewScaffoldTestBatchAsync` (line 92), `PreviewScaffoldTestAsync` (line 331), and `PreviewScaffoldFirstTestFileAsync` (line 458). Six private nested types — `BatchScaffoldContext` (line 268), `BatchScaffoldState` (line 277), `ResolvedTargetTypeInfo` (line 294), `InterfaceResolutionResult` (line 1472), `SiblingTestPattern` (line 1869), `SiblingInferenceResult` (line 1880) — are scattered throughout the file, sharing no cross-type visibility requirements preventing a partial-class split. All callers depend on `IScaffoldingService` (6 references in `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs` and `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`) — none import the implementation class directly, so a file reorganization is invisible to callers. The backlog row proposes four partial files (one per public method group) which would touch 5 production files (original + 4 new), exceeding Rule 3's 4-file hard cap. Scoped to Rule 3: original + 3 partials, grouping `PreviewScaffoldTestBatchAsync` and `PreviewScaffoldFirstTestFileAsync` together in one partial (they share the test-framework builder methods at lines 770/832/888 and the sibling-pattern inference helpers). Anchor stale note: backlog row cited 2521 LOC; current file is 2776 lines (verified). All cited method entry points confirmed. |
| Approach | 1. Add `partial` keyword to `ScaffoldingService` class declaration in `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`; remove the three method groups being split out, retaining: constructor, DI fields, shared private helpers (`ResolveProject`, `ValidateIsTestProject`, `ResolveTestFramework`, `StripToSimpleTypeName`, `ResolveFolderSegmentsForNamespace`), and all nested types (`BatchScaffoldContext`, `BatchScaffoldState`, `ResolvedTargetTypeInfo`, `InterfaceResolutionResult`, `SiblingTestPattern`, `SiblingInferenceResult`). Nested types stay in the base partial to avoid cross-file accessibility problems. 2. Create `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TypePreview.cs` — `partial class ScaffoldingService` containing `PreviewScaffoldTypeAsync` (line 38) and its private helpers `BuildTypeContent`, `ResolveInterfaceMembersAsync`, `BuildInterfaceMembers`, `BuildMemberStub`, `FormatParameter`. 3. Create `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs` — `partial class ScaffoldingService` containing `PreviewScaffoldTestAsync` (line 331) and its private helpers: `ResolveTargetTypeAndMethodAsync`, `InferSiblingTestPattern`, `SuggestSampledTestNameAsync`, `BuildTestContent`, `BuildTestXunit`, `BuildTestNUnit`, `BuildTestMSTest`, `BuildSiblingFragments`, `InferSiblingPatternFromRecent`, `CollectSiblingTestMethodNames`, `FormatMethodSignature`, `CombineWarnings`. 4. Create `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs` — `partial class ScaffoldingService` containing `PreviewScaffoldTestBatchAsync` (line 92) with its batch-scaffolding helpers AND `PreviewScaffoldFirstTestFileAsync` (line 458) with its per-framework builders (`BuildFirstTestFileMSTest` line 770, `BuildFirstTestFileXunit` line 832, `BuildFirstTestFileNUnit` line 888) and helpers. These two public methods share the per-framework builder helper family and sibling-inference calls, making them the natural grouping when the 4-file budget forces consolidation. Each partial file carries the same `using` block currently at the top of `ScaffoldingService.cs`. Pure code organization; zero behavior change. |
| Scope | Production files modified/created: 4. (1) `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` — modified (add `partial`, remove split-out methods). (2) `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TypePreview.cs` — new partial. (3) `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs` — new partial. (4) `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs` — new partial. Rule 3 satisfied (≤ 4 production files). No Rule 3 exemption claimed. Test files modified: 0 new files; `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs` must remain untouched and continue to pass. |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) Nested types must stay in the base partial — all 6 are `private` or `internal sealed` and may be referenced by multiple method groups. Executor must verify each nested type is referenced only within one method group before moving; if cross-referenced, keep it in the base. (2) Each new partial file needs the same `using` block — a missed `using` will produce a compile error caught immediately by `mcp__roslyn__compile_check`. (3) SDK-style `.csproj` uses `<Compile Include="**/*.cs" />` glob — new `.cs` files under `Services/` will be picked up automatically without a project-file edit. Confirm this assumption with `compile_check` before closing. (4) Weak evidence: no active correctness driver. File has grown 2521 → 2776 lines, marginally strengthening the organization argument, but if a correctness bug appears in `ScaffoldingService.cs` while this initiative is in progress, that correctness bug takes priority. (5) Phase 2 (extract per-framework builders into `TestFrameworkScaffolder` strategy) is explicitly deferred to a separate row — do NOT implement Phase 2 in this initiative. |
| Validation | (1) `mcp__roslyn__compile_check` (or `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`) produces zero errors and warnings. (2) `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs` passes unchanged with `mcp__roslyn__test_run --filter ScaffoldingIntegration`. No test modifications permitted — the regression shape is the existing suite green. (3) Check `ScaffoldingService` still shows as a single class symbol in `mcp__roslyn__symbol_search` (partial keyword is transparent to Roslyn's symbol model). (4) `eng/verify-ai-docs.ps1` passes (no doc-link regressions). |
| Performance review | N/A — pure file organization, no runtime behavior change. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | `ScaffoldingService.cs` split into three partial files by scaffold type (`ScaffoldingService.TypePreview.cs`, `ScaffoldingService.TestPreview.cs`, `ScaffoldingService.TestBatchAndFirstTestPreview.cs`) — pure code organization, no behavior change. |
| Backlog sync | Close rows: [`scaffolding-service-split-by-scaffold-type`]. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**
- **Pattern coordinates:** No in-repo exemplar for sealed instance class split; use `ServerSurfaceCatalog.*.cs` family (e.g. `ServerSurfaceCatalog.Editing.cs:3`) for naming/header template. `IScaffoldingService` interface list goes ONLY on primary file.
- **Hotspot seam — base file retains:** constructor (:28-36), 3 DI fields (:24-26), `MinimalSymbolDisplayExtensions` (:12-20), ALL 6 nested types (BatchScaffoldContext:268, BatchScaffoldState:277, ResolvedTargetTypeInfo:294, InterfaceResolutionResult:1472, SiblingTestPattern:1869, SiblingInferenceResult:1880), shared helpers called cross-method-group.
- **Edge cases:** (1) `MinimalSymbolDisplayExtensions` is top-level static class — do NOT move it; (2) preserve `PreviewScaffoldTestAsync` optional parameter; (3) switch dispatch at :763-767 — keep all `Build*FirstTestFile*` together; (4) run `find_references` on helpers before moving.
- **Negative space:** don't add `: IScaffoldingService` to sibling partials; don't move `MinimalSymbolDisplayExtensions`; don't touch `IScaffoldingService.cs`, `ScaffoldingIntegrationTests.cs`, or `ServiceCollectionExtensions.cs`; no `#region` markers.

</details>
