# Backlog sweep plan — 20260426T025255Z

<!-- purpose: In-repo planning pass against the 13-row open backlog after the 20260424T041204Z sweep finalized. -->
<!-- scope: in-repo -->

Produced by `ai_docs/prompts/backlog-sweep-plan.md` v4 against the post-20260424
backlog (0 P2 + 6 P3 + 7 P4 = 13 open rows). Initiatives are ordered per Step 5
(correctness-class desc, context-cost asc within band, catalog distribution).

**Anchor verification:** spot-checked at plan-draft time. Verified live:
`src/RoslynMcp.Roslyn/Services/PreviewStore.cs`,
`src/RoslynMcp.Roslyn/Services/ReferenceService.cs`,
`src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog*.cs` (9 partials),
`src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`,
`src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`,
`hooks/hooks.json`, `.claude/agents/pr-reconciler.md`. Wider grep at plan time
flipped two row assumptions (see initiatives 1 and 10–11). Full per-symbol
anchor pass deferred per Step 3 cost-constrained-sweep escape hatch.

**Adjacent perf-smell pass:** skipped at plan-draft time (cost-constrained).
Executors should run `roslyn-mcp:review` / `complexity` on touched files during
Step 3 of the execute prompt and file any incidental perf findings as new
backlog rows rather than bundling.

**Batch-shape overview:**
- 0 P2 initiatives — open queue is correctness-mature; previous sweep cleared the P2 band.
- 6 P3 initiatives — 1 correctness, 3 UX (incl. 2 deferred), 2 hook-config edits.
- 7 P4 initiatives — 5 active polish/UX, 2 deferred (heroic-last + heroic-split).
- 4 deferred from the start (heroic-split or evidence-gated): `pretooluse-apply-preview-token-invariant`, `tool-annotation-population-audit`, `ci-test-parallelization-audit` (dedicated phased plan), `workspace-process-pool-or-daemon` (heroic-last).
- 9 active initiatives are in scope for this sweep wave-rotation.

**Catalog-touching rows** (schedule across waves per Step 5 sort rule 4):
order 3 (`roslyn-mcp-sister-tool-name-aliases` — alias entries),
order 4 (`find-type-consumers-file-granularity-rollup` — new-tool registration),
order 9 (`semantic-grep-identifier-scoped-search` — new-tool registration).
Orders 3 and 4 are adjacent because the cost-ASC band ordering inside P3-UX
forces them into this sequence — the orchestrator MUST split them across
different waves per executor §2a (one catalog-touching initiative per wave).

**WorkspaceManager-touching rows:** none in this batch.

**No-shims policy reminder (session-wide):** the orchestration prompt rejects
`shims-added: >0` with weak justification. Each initiative below explicitly
calls out where breaking changes will land instead of shim layers — every plan
field already assumes the no-backcompat constraint.

**Final todo (all initiatives):** `backlog: sync ai_docs/backlog.md` per agent contract.

---

### 1. `stdio-host-stdout-audit` — Preventive analyzer + test for stdout-write invariants

| Field | Content |
|-------|---------|
| **Status** | merged (PR #449, 2026-04-26) |
| **Backlog rows closed** | `stdio-host-stdout-audit` |
| **Diagnosis** | MCP best-practices (2026-04 ref): stdio servers must NOT write to stdout — the framing channel shares it. Plan-draft grep over `src/RoslynMcp.Host.Stdio/` for `Console\.Write\|Console\.Out\.\|stdout\.Write\|Trace\.WriteLine` returned **4 hits, all in `Program.cs`, all `Console.Out.Flush()`** (lines 20, 145, 156, 157). Flush is protocol-correct — stdio framing requires explicit flush after ND-JSON write — NOT a stdout-write violation. So the audit reveals zero current leaks; work scope shifts from "find and fix" to "preventive invariant gate" per the row's last clause ("Add an analyzer or test that asserts no direct stdout writes in `src/RoslynMcp.Host.Stdio/**/*.cs`"). |
| **Approach** | Add a new Roslyn analyzer (`StdoutWriteAnalyzer`) under `analyzers/ServerSurfaceCatalogAnalyzer/` that flags `Console.Write*`, `Console.Out.Write*`, `Console.Out.WriteLine*`, `Console.Error.Write*`, `stdout.Write*`, `Trace.WriteLine` calls in `RoslynMcp.Host.Stdio.dll`. Allow-list `Console.Out.Flush()` / `FlushAsync()` (protocol framing); allow-list `Console.Error.*` (stderr is fine for stdio servers). Diagnostic id: RMCP010. Update `analyzers/ServerSurfaceCatalogAnalyzer/AnalyzerReleases.Unshipped.md` to register the new diagnostic. Add a regression test asserting current `Program.cs` contents pass and a synthetic `Console.WriteLine("test")` fails. |
| **Scope** | Production 2: `analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs` (new), `analyzers/ServerSurfaceCatalogAnalyzer/AnalyzerReleases.Unshipped.md`. Tests 1: new analyzer test file (e.g. `tests/RoslynMcp.Tests/Analyzers/StdoutWriteAnalyzerTests.cs`). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 28000 |
| **Risks** | Analyzer false-positives on legitimate stderr writes — narrow the rule to receivers `Console.Out` / `Console` / `stdout`. Allow-list `Flush*` explicitly; flushes are protocol-correct. |
| **Validation** | New analyzer test passes; build green; introducing a `Console.WriteLine` in `Program.cs` produces RMCP010 diagnostic; removing it returns green. |
| **Performance review** | N/A — analyzer-only. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** RMCP010 analyzer that prevents `Console.Out.Write*` / `Trace.WriteLine` from leaking into `RoslynMcp.Host.Stdio` (would corrupt the stdio NDJSON framing). Allow-listed `Console.Out.Flush*` (protocol-required) and `Console.Error.*` (stderr-safe). (`stdio-host-stdout-audit`) |
| **Backlog sync** | Close rows: `stdio-host-stdout-audit`. Mark obsolete: none. Update related: none. |

---

### 2. `pretooluse-block-release-critical-file-edits` — Hook-side guard for release-managed files

| Field | Content |
|-------|---------|
| **Status** | merged (PR #448, 2026-04-26) |
| **Backlog rows closed** | `pretooluse-block-release-critical-file-edits` |
| **Diagnosis** | Accidental agent edits to release-managed files (`Directory.Build.props`, `BannedSymbols.txt`, the 5 version files, `hooks/hooks.json` itself, `eng/verify-skills-are-generic.ps1`) have shipped drift historically (cross-ref: 1.28.0 parallel-PR changelog-append friction). No PreToolUse guard exists today — drift is caught at CI gate after the agent has already shipped. Row cites `hooks/hooks.json` as the single anchor. |
| **Approach** | Add a new PreToolUse entry in `hooks/hooks.json` with `matcher: "Edit\|Write\|MultiEdit"` and a prompt-style hook that inspects the target path. Block when path matches the release-managed set (regex over: `Directory.Build.props$`, `BannedSymbols.txt$`, the 5 version files, `\.claude-plugin/(plugin\|marketplace)\.json$`, `eng/verify-version-drift\.ps1$`, `hooks/hooks\.json$`, `eng/verify-skills-are-generic\.ps1$`). Allow override with the literal phrase `ack: release-managed` in the agent's edit rationale. Document in `ai_docs/workflow.md` § *Release-managed file guard*. |
| **Scope** | Production 2: `hooks/hooks.json`, `ai_docs/workflow.md`. Tests 0 (hook prompts have no programmatic test surface — the verification is manual: try editing `Directory.Build.props` without the override phrase). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **Risks** | Override phrase too easy to bypass — keep it phrase-based to force agents to acknowledge the policy in their rationale. The guard is advisory; CI gate (`verify-version-drift.ps1`) remains authoritative. |
| **Validation** | Manual: in a fresh session, attempt `Edit src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` without the override phrase → blocked with actionable message. Re-run with `ack: release-managed` in rationale → proceeds. |
| **Performance review** | N/A — hook config. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** PreToolUse guard in `hooks/hooks.json` that blocks `Edit`/`Write`/`MultiEdit` on release-managed files (Directory.Build.props, BannedSymbols.txt, the 5 version files, plugin/marketplace JSON, hooks.json, verify-skills script) unless the agent's rationale contains `ack: release-managed`. Documented in `ai_docs/workflow.md`. (`pretooluse-block-release-critical-file-edits`) |
| **Backlog sync** | Close rows: `pretooluse-block-release-critical-file-edits`. Mark obsolete: none. Update related: none. |

---

### 3. `roslyn-mcp-sister-tool-name-aliases` — Alias canonical Roslyn tools to common python-refactor names

| Field | Content |
|-------|---------|
| **Status** | merged (PR #450, 2026-04-26) |
| **Backlog rows closed** | `roslyn-mcp-sister-tool-name-aliases` |
| **Diagnosis** | Agents repeatedly call Roslyn tools with python-refactor names and hit `<tool_use_error>Error: No such tool available: mcp__roslyn__<name></tool_use_error>` — 6 occurrences in the 2026-04-10→04-24 retro across `get_symbol_outline` (→ `document_symbols`), `find_duplicated_code` (→ `find_duplicated_methods` / `find_duplicate_helpers`), `get_test_coverage_map` (→ `test_coverage`). Anchor: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (and 8 partials by category). |
| **Approach** | Register thin alias `[McpServerTool]` methods that delegate to canonical implementations. For each alias, the tool wrapper returns the canonical response shape with an additional top-level `deprecation: { canonicalName: "<name>", reason: "alias for cross-MCP-server name compatibility" }` field so agents see the migration path inline. Aliases land in `ServerSurfaceCatalog.Symbols.cs` / `.Analysis.cs` partials matching the canonical tool's category, and in the corresponding `Tools/*.cs` file as thin delegating methods. |
| **Scope** | Production 2-3: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Symbols.cs`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs`, and 1-2 corresponding `Tools/*.cs` files (likely `SymbolTools.cs` and `AnalysisTools.cs`). Tests 1: `AliasToolsTests.cs` — assert `get_symbol_outline` returns the same envelope as `document_symbols` plus the deprecation field. CATALOG-TOUCHING: orchestrator must place this in a wave with NO other catalog-toucher. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | Alias proliferation — keep the list to the 3 named in the row evidence (`get_symbol_outline`, `find_duplicated_code`, `get_test_coverage_map`). Future aliases require new backlog rows. The deprecation field is part of the canonical envelope shape (no shim wrapper). |
| **Validation** | `mcp__roslyn__get_symbol_outline` succeeds on a real workspace and returns same data as `mcp__roslyn__document_symbols`; deprecation field present in alias responses, absent in canonical responses. |
| **Performance review** | N/A — pure delegation, no hot path. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** Cross-server tool aliases (`get_symbol_outline` → `document_symbols`, `find_duplicated_code` → `find_duplicated_methods`/`find_duplicate_helpers`, `get_test_coverage_map` → `test_coverage`) so agents migrating from python-refactor (Jedi) land on the canonical tool. Aliases include `deprecation.canonicalName` in the response envelope. (`roslyn-mcp-sister-tool-name-aliases`) |
| **Backlog sync** | Close rows: `roslyn-mcp-sister-tool-name-aliases`. Mark obsolete: none. Update related: none. |

---

### 4. `find-type-consumers-file-granularity-rollup` — New `find_type_consumers` MCP tool

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `find-type-consumers-file-granularity-rollup` |
| **Diagnosis** | Agents looking for "which files touch this type" fall back to Grep (≥5 refactor sessions in the 2026-04-10→04-24 retro: 57a0d696, bfe7443f, ac8b4d93, 08adc1f1, b3e4cb62) because `find_references` returns per-site hits with no per-file rollup. Row cites `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (reuse reference index) and a new tool registration in `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. |
| **Approach** | New `find_type_consumers(typeName, limit) → [{filePath, kinds: [using\|ctor\|inherit\|field\|local], count}]` MCP tool. Implementation reuses the existing reference index in `ReferenceService` — group results by file path, classify each site's `kind` (using-directive / ctor invocation / inherit / field declaration / local declaration) via syntax-node walk, return aggregated rollup. Standard 3-layer pattern: Core contract DTO, Roslyn service method, Host.Stdio tool wrapper, catalog entry, DI line. |
| **Scope** | Rule 3 exemption: new-MCP-tool structural-unit (4 units, ~7 files including addenda). Structural units: (1) Core contract `src/RoslynMcp.Core/Models/TypeConsumersResultDto.cs` + interface change in `ITypeConsumersService` if present (or extend `ISymbolService`); (2) Roslyn service method on `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (or new `TypeConsumersService.cs` if separation is cleaner); (3) Host.Stdio tool surface `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`; (4) Registration `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Symbols.cs` + `ServiceCollectionExtensions.cs` DI line. Addenda: `tests/RoslynMcp.Tests/TestBase.cs` DI registration; `README.md` surface-count bump (`162 → 163` tools, experimental count `54 → 55`). Tests 1: `TypeConsumersServiceTests.cs` covering using-directive / ctor / inherit / field / local kinds. CATALOG-TOUCHING: orchestrator MUST schedule into a different wave than initiative 3. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 55000 |
| **Risks** | Kind classification edge cases (generic constraints, `typeof`, attribute references). Cap at the 5 named kinds; surface `kind: "other"` for unrecognized contexts rather than fail. |
| **Validation** | New tool returns rollup on real workspace; `compile_check` / `test_run` green; `ReadmeSurfaceCountTests` green after `README.md` bump. |
| **Performance review** | Hot path — touches reference index. Verify `find_references_bulk` perf isn't degraded (new tool runs the same scan; aggregation is O(N) over hits). |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `find_type_consumers(typeName, limit) → [{filePath, kinds, count}]` MCP tool — file-granularity rollup over the existing reference index, removes the Grep fallback for "which files touch this type" workflows. Surface count bumped to **163 tools** (108 stable / 55 experimental). (`find-type-consumers-file-granularity-rollup`) |
| **Backlog sync** | Close rows: `find-type-consumers-file-granularity-rollup`. Mark obsolete: none. Update related: none. |

---

### 5. `pr-reconciler-poll-budget-tightness` — Lengthen reconciler check-poll loop

| Field | Content |
|-------|---------|
| **Status** | merged (PR #447, 2026-04-26) |
| **Backlog rows closed** | `pr-reconciler-poll-budget-tightness` |
| **Diagnosis** | The `pr-reconciler` subagent's `BLOCKED-with-pending-checks` rule (`.claude/agents/pr-reconciler.md:38`) is "wait 60s, retry once; if still not green, emit STATUS: not-ready and exit" — ~120s effective ceiling, but this repo's `validate` job (full Release-mode test run) routinely takes 8-12 min. The reconciler returned `not-ready` on every initiative-PR's first dispatch, forcing the orchestrator to bash-poll then re-dispatch (observed twice in the 2026-04-25 sweep: PRs #441 and #442). Anchor: `.claude/agents/pr-reconciler.md` (rule table at L38; gh-failure rule at L107). |
| **Approach** | Replace the single retry with a bounded backoff loop in the rule table: "wait 60s, retry up to 12 times (12-min ceiling) with progress notifications between attempts; if still not green after 12 retries, emit STATUS: not-ready". Keep the early-exit path for genuinely-failing checks (any check `fail`/`error`) — only widen the budget for `IN_PROGRESS` / `PENDING`. Update the documentation row in the rule table. |
| **Scope** | Production 1: `.claude/agents/pr-reconciler.md`. Tests 0 (subagent prompt change — no programmatic test surface; verification is observational on the next sweep). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 18000 |
| **Risks** | 12-min poll ceiling is generous — if the orchestrator dispatches 4 reconcilers in parallel and each waits the full 12 min, total wall-clock cost rises. Acceptable trade: the orchestrator already serializes reconcilers per PR; a single reconciler waiting 12 min is cheaper than the orchestrator re-dispatching. |
| **Validation** | Manual: on the next sweep, observe a parallel batch where the second-to-merge PR has `IN_PROGRESS` checks at first reconciler dispatch — reconciler should now wait through the validation run instead of returning `not-ready` after 120s. |
| **Performance review** | N/A — agent-prompt edit. |
| **CHANGELOG category** | `Maintenance` |
| **CHANGELOG entry (draft)** | - **Maintenance:** Widen `pr-reconciler` subagent's pending-checks poll budget from 1×60s retry (~120s ceiling) to 12×60s retries (~12-min ceiling) with progress notifications. Failing checks still short-circuit the early-exit path. Removes false-`not-ready` returns on slow validate jobs. (`pr-reconciler-poll-budget-tightness`) |
| **Backlog sync** | Close rows: `pr-reconciler-poll-budget-tightness`. Mark obsolete: none. Update related: none. |

---

### 6. `posttool-verify-skills-on-shipped-skill-edits` — PostToolUse hook for shipped-skill linter

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `posttool-verify-skills-on-shipped-skill-edits` |
| **Diagnosis** | `eng/verify-skills-are-generic.ps1` is gated by `just ci` + `verify-release.ps1`, so an `ai_docs/` / `eng/` / `backlog.md` leak inside a `./skills/**/SKILL.md` is caught at CI, not at edit-time — round-trip waste per retro observation. Anchor: `hooks/hooks.json`. |
| **Approach** | Add a new PostToolUse entry in `hooks/hooks.json` matching `Edit\|Write\|MultiEdit` with a path filter `^skills/.*\.md$` (relative to repo root). On match, invoke `pwsh eng/verify-skills-are-generic.ps1` and surface violations the same turn. Use the `command` hook type (not `prompt` — the script is fast and deterministic; output should be displayed verbatim). Timeout-bounded (default ~5s; the script is <1s on this repo). |
| **Scope** | Production 1: `hooks/hooks.json`. Tests 0 (hook config — manual verification: edit a `skills/*/SKILL.md` to add a forbidden token like `ai_docs/`, confirm hook surfaces violation immediately). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **Risks** | Hook fires on every skill edit — must be fast. Verified script runtime is <1s; acceptable. False-positive risk: the verify script's regex might flag legitimate URLs that resemble forbidden tokens — current script already allow-lists GitHub URLs pointing at this repo's public docs (per AGENTS.md L15); no script change needed. |
| **Validation** | Manual: edit `skills/analyze/SKILL.md` to insert a string `ai_docs/`, confirm hook reports the violation; remove the string, confirm clean. |
| **Performance review** | Hook adds <1s to skill-edit latency; acceptable. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** PostToolUse hook in `hooks/hooks.json` that runs `eng/verify-skills-are-generic.ps1` on every `skills/**/SKILL.md` edit, surfacing violations the same turn instead of waiting for CI. (`posttool-verify-skills-on-shipped-skill-edits`) |
| **Backlog sync** | Close rows: `posttool-verify-skills-on-shipped-skill-edits`. Mark obsolete: none. Update related: none. |

---

### 7. `skills-remove-server-heartbeat-polling` — Replace heartbeat polls with single-shot status checks

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `skills-remove-server-heartbeat-polling` |
| **Diagnosis** | The `/roslyn-mcp:complexity` skill (and any wait-loop in sibling skills under `skills/`) polls `mcp__roslyn__server_heartbeat` in a loop — one session in the 2026-04-10→04-24 retro (b5464409) logged 78 heartbeat calls inside a single `/complexity` invocation; two others logged 8 and 6. Plan-draft grep over `skills/` returns **17 skill files mentioning `server_heartbeat`** — but most reference it once for the `state in {idle, ready}` precheck (introduced in PR #393, `connection-state-ready-unsatisfiable-preload`), not in a poll loop. The actual offenders are skills that loop with a `wait`/`retry` instruction around the heartbeat call. |
| **Approach** | Audit all 17 hits. For each, classify as (a) single precheck — keep as-is, this is the canonical pattern post-PR #393; (b) poll-loop — replace with a single `server_info` (or `workspace_status`) check followed by bounded backoff if the user explicitly named "wait until ready". Replace any "while not ready: heartbeat; sleep" pattern with "if not ready after one check, ask user / abort with actionable message". Update each offender's SKILL.md with the corrected pattern. Cap edits at ≤4 SKILL.md files per Rule 3 — if more files actually need changes, split into a follow-up initiative. |
| **Scope** | Production ≤4: subset of `skills/*/SKILL.md` (audit-determined; expected ≤4 — primary suspects are `complexity`, `format-sweep`, `modernize`, possibly `architecture-review` which are the heaviest tool runners). Tests 0 (skill prompts have no programmatic test; verification is observational on the next /complexity invocation — heartbeat call count drops from 78 to 1). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 30000 |
| **Risks** | Audit may discover >4 offenders — if so, the executor must split. The 4-cap protects budget; the row's evidence (3 sessions, 78/8/6 hits) suggests ≤3 heavy offenders. Soft contract: if audit finds 5-6, the executor opens new backlog rows for the overflow rather than blowing the file budget. |
| **Validation** | After changes, run `/roslyn-mcp:complexity` end-to-end and confirm `mcp__roslyn__server_heartbeat` is called ≤1 time per skill invocation. |
| **Performance review** | N/A — skill-prompt edit; perf benefit is reduction in transcript noise (heartbeat call cost is ~1ms/call but the 78× pattern bloats transcript by ~10K tokens/skill invocation). |
| **CHANGELOG category** | `Maintenance` |
| **CHANGELOG entry (draft)** | - **Maintenance:** Removed `server_heartbeat` poll-until-ready loops from shipped skills under `skills/`; replaced with single-shot `server_info` precheck. Cuts transcript bloat from 78 heartbeat calls per `/complexity` invocation down to ≤1. (`skills-remove-server-heartbeat-polling`) |
| **Backlog sync** | Close rows: `skills-remove-server-heartbeat-polling`. Mark obsolete: none. Update related: none. |

---

### 8. `response-format-json-markdown-parameter` — Markdown response option for `validate_workspace`

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `response-format-json-markdown-parameter` |
| **Diagnosis** | MCP best-practices: summary-shaped tools should support `response_format: "json" \| "markdown"` (default `json` preserves wire compatibility). Current state: all 162 tools return JSON only. Highest-value candidate per row: `validate_workspace` (typically 120-line JSON → ~30-line table). Anchors: `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` (verified live). Other candidates (`server_info`, `workspace_list`, `workspace_status`, `symbol_impact_sweep`, `project_diagnostics(summary)`) are out of scope for this initiative — they get their own rows after this one ships and validates the pattern. |
| **Approach** | Narrow scope to `validate_workspace` only (Rule 3 file budget). Add `responseFormat?: "json" \| "markdown"` parameter to the tool method in `ValidationBundleTools.cs`. Build a `ValidateWorkspaceMarkdownFormatter.cs` helper that takes the existing `ValidateWorkspaceResponseDto` and emits a compact markdown summary table (status / error count / warning count / per-test rollup). Default behavior unchanged (json). |
| **Scope** | Production 2: `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`, `src/RoslynMcp.Host.Stdio/Formatters/ValidateWorkspaceMarkdownFormatter.cs` (new). Tests 1: `ValidateWorkspaceMarkdownFormatterTests.cs` covering clean / compile-error / test-failure / mixed responses. Out of scope (track as new follow-up rows): `server_info`, `workspace_list`, `workspace_status`, `symbol_impact_sweep`, `project_diagnostics(summary)`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 38000 |
| **Risks** | Two output shapes are a maintenance tax — only ship for tools with measurable context savings (>50% reduction). For `validate_workspace` the typical 120-line JSON → 30-line markdown is exactly that ratio. |
| **Validation** | `validate_workspace --responseFormat=markdown` returns a markdown table; `--responseFormat=json` (or omitted) returns the existing JSON envelope; both modes produce equivalent verdicts on the same workspace state. |
| **Performance review** | N/A — formatter is single-pass O(N) over response items. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `validate_workspace` accepts `responseFormat: "json" \| "markdown"` (default `json`); markdown shape produces a compact ~30-line summary table vs the typical 120-line JSON envelope. Other summary-shaped tools (`server_info`, `workspace_list`, etc.) will adopt the pattern in follow-up rows. (`response-format-json-markdown-parameter`) |
| **Backlog sync** | Close rows: `response-format-json-markdown-parameter`. Mark obsolete: none. Update related: open follow-up rows for `server_info` / `workspace_list` / `workspace_status` / `symbol_impact_sweep` / `project_diagnostics` markdown formatters once this lands. |

---

### 9. `semantic-grep-identifier-scoped-search` — New `semantic_grep` MCP tool

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `semantic-grep-identifier-scoped-search` |
| **Diagnosis** | Agents want to grep inside C# code but exclude string/comment matches — current Grep returns hits inside CHANGELOG, markdown, and string literals (3 Roslyn-self-audit sessions in the 2026-04-10→04-24 retro: 57a0d696, 08adc1f1, cf2d0b85). Row classifies as P4 with weak evidence (3 self-audit sessions, no external-repo reproduction). Anchors: new service under `src/RoslynMcp.Roslyn/Services/`, new tool in `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. |
| **Approach** | New `semantic_grep(pattern, scope=identifiers\|strings\|comments\|all, projectFilter?) → [{filePath, line, column, tokenKind, snippet}]` MCP tool. Implementation walks `SyntaxTree.DescendantTokens` per workspace document, filters by `SyntaxToken.Kind()` (identifier vs string-literal vs trivia), runs the regex against matching tokens, returns ranked hits. Standard 3-layer pattern. |
| **Scope** | Rule 3 exemption: new-MCP-tool structural-unit (4 units, ~7 files including addenda). Structural units: (1) Core contract `src/RoslynMcp.Core/Models/SemanticGrepResultDto.cs` + service interface; (2) Roslyn service `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs` (new); (3) Host.Stdio tool surface `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`; (4) Registration `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs` + `ServiceCollectionExtensions.cs` DI line. Addenda: `tests/RoslynMcp.Tests/TestBase.cs` DI registration; `README.md` surface-count bump (`163 → 164` after initiative 4 lands; `162 → 163` if initiative 4 hasn't landed yet — orchestrator will resolve at Step 5 reconcile). Tests 1: `SemanticGrepServiceTests.cs` covering each scope value. CATALOG-TOUCHING. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 55000 |
| **Risks** | Token-walk over a large solution is O(documents × tokens). Cap returned hits to a sane limit (e.g. 500) to prevent runaway responses; document the cap in the tool description. P4-weak-evidence: if external-repo sessions don't reproduce the pattern within the next 30 days, this initiative may be reverted in a follow-up cleanup. |
| **Validation** | `semantic_grep("Console.Write", scope=identifiers)` returns hits in actual code; `scope=strings` returns hits in string-literal arguments; `scope=all` returns both. Compare against `Grep` output for the same pattern — semantic_grep should miss hits inside `// comments` when `scope=identifiers`. |
| **Performance review** | Hot path — walks every token. Capture P95 on a 50-project solution; cap responses to bounded size. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `semantic_grep(pattern, scope=identifiers\|strings\|comments\|all, projectFilter?)` MCP tool — token-aware grep over C# code, lets agents avoid false-positive matches inside string literals or comments. Capped at 500 hits/call. Surface count bumped accordingly. (`semantic-grep-identifier-scoped-search`) |
| **Backlog sync** | Close rows: `semantic-grep-identifier-scoped-search`. Mark obsolete: none. Update related: none. |

---

### 10. `pretooluse-apply-preview-token-invariant` — Hook validates preview tokens (DEFERRED — heroic split)

| Field | Content |
|-------|---------|
| **Status** | deferred (heroic — needs split before execution) |
| **Backlog rows closed** | (none — initiative deferred) |
| **Diagnosis** | The `PreToolUse:mcp__roslyn__*_apply` hook scans the session transcript for a matching `*_preview` and false-positive-blocks `*_apply` 8 times across 4 refactor sessions in the 2026-04-10→04-24 retro. Row anchors: `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`, `src/RoslynMcp.Host.Stdio/Tools/*Apply*.cs`, `hooks/hooks.json`. **Plan-draft grep result:** `*_apply` MCP tool methods are spread across **25+ files** in `src/RoslynMcp.Host.Stdio/Tools/` (e.g. `RefactoringTools.cs`, `ScaffoldingTools.cs`, `EditTools.cs`, `FixAllTools.cs`, `BulkRefactoringTools.cs`, etc.). Adding a required `previewToken` parameter to every `*_apply` method violates Rule 3 file budget by ≥6× the cap. |
| **Approach** | (Plan-draft assessment.) Splitting into shippable initiatives requires either (a) a dedicated phased plan like `ai_docs/plans/<ts>_apply-preview-token-invariant/` with 6-7 phases, each touching one tool family (refactoring applies, edit applies, scaffold applies, etc.), or (b) a single architectural initiative that adds the parameter at the dispatch layer (`Tools/ToolDispatch.cs`) instead of per-tool — needs design pass to confirm this is viable given the per-tool MCP-attribute schema. Either path is heroic for a single sweep slot. |
| **Scope** | (deferred) — would touch ~25 prod files + hook config + PreviewStore. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | (heroic; not estimated) |
| **Risks** | Without splitting, executor will refuse at Rule 3 vetting. |
| **Validation** | (deferred) |
| **Performance review** | (deferred) |
| **CHANGELOG category** | (deferred) |
| **CHANGELOG entry (draft)** | (deferred) |
| **Backlog sync** | Initiative deferred — backlog row stays open. Recommended next step: open a dedicated phased plan via `/backlog-intake` heroic-split or hand-author a new plan directory. |

---

### 11. `tool-annotation-population-audit` — Populate MCP annotation hints (DEFERRED — heroic split)

| Field | Content |
|-------|---------|
| **Status** | deferred (heroic — needs split before execution) |
| **Backlog rows closed** | (none — initiative deferred) |
| **Diagnosis** | MCP best-practices: tools should declare `readOnlyHint` / `destructiveHint` / `idempotentHint` / `openWorldHint` annotations. **Plan-draft grep result:** **0 occurrences** of these field names across `src/` — the entire 162-tool surface lacks annotations. Row says: "every `*_apply` → `destructiveHint: true`; every `*_preview` / read-shape tool → `readOnlyHint: true`; workspace-scoped tools → `openWorldHint: false`". Populating across all 162 tools would touch most of the 25+ `Tools/*.cs` files. Violates Rule 3 by ≥6×. |
| **Approach** | (Plan-draft assessment.) Best path is a Roslyn analyzer (RMCP011) that REQUIRES the hints on every `[McpServerTool]` method, then fix-all the existing surface in batches. Phased plan with: phase 1 = analyzer + first batch (~20 tools); phase 2-N = subsequent batches. Each batch fits within Rule 3. Alternative: add hints centrally via dispatch metadata (less likely — MCP attribute schema requires per-method declaration). |
| **Scope** | (deferred) — full surface change; needs phased plan. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | (heroic; not estimated) |
| **Risks** | Same as initiative 10 — without splitting, executor refuses at Rule 3 vetting. |
| **Validation** | (deferred) |
| **Performance review** | (deferred) |
| **CHANGELOG category** | (deferred) |
| **CHANGELOG entry (draft)** | (deferred) |
| **Backlog sync** | Initiative deferred — backlog row stays open. Recommended next step: dedicated phased plan with analyzer + per-batch initiatives. |

---

### 12. `ci-test-parallelization-audit` — Cleanup-race flakes (DEFERRED — dedicated phased plan)

| Field | Content |
|-------|---------|
| **Status** | deferred (dedicated phased plan exists) |
| **Backlog rows closed** | (none — initiative deferred to dedicated plan) |
| **Diagnosis** | Refresh the old test-parallelization audit around the real residual issue: `eng/verify-release.ps1` still has 3 pre-existing cleanup-race flakes even though the `[DoNotParallelize]` sweep is already done. Anchors: `tests/RoslynMcp.Tests/AssemblyCleanup.cs`, `tests/RoslynMcp.Tests/TestBase.cs`. |
| **Approach** | The dedicated phased plan at `ai_docs/plans/20260422T170500Z_test-parallelization-audit/plan.md` owns the localization + fix phases. This sweep does not duplicate that work. |
| **Scope** | (deferred) — owned by dedicated plan. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | (deferred) |
| **Risks** | (deferred) |
| **Validation** | (deferred) |
| **Performance review** | (deferred) |
| **CHANGELOG category** | (deferred) |
| **CHANGELOG entry (draft)** | (deferred) |
| **Backlog sync** | Initiative deferred — backlog row remains open; phased plan owns closure. |

---

### 13. `workspace-process-pool-or-daemon` — Daemon investigation (DEFERRED — heroic-last)

| Field | Content |
|-------|---------|
| **Status** | deferred (heroic-last; profile-evidence gated) |
| **Backlog rows closed** | (none — initiative deferred until profile evidence lands) |
| **Diagnosis** | Investigation phase remains gated on large-solution profile evidence (`docs/large-solution-profiling-baseline.md`). Per row: do not start a daemon/process-pool implementation blindly. First capture representative 50+ project timings; only if `workspace_load`/reload P95 still blocks daily use after `workspace_warm` should the next plan produce a bounded design note. |
| **Approach** | No work this sweep. Reactivate only when 50+ project workspace_load timings land. |
| **Scope** | (deferred) |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | (deferred) |
| **Risks** | (deferred) |
| **Validation** | (deferred) |
| **Performance review** | (deferred) |
| **CHANGELOG category** | (deferred) |
| **CHANGELOG entry (draft)** | (deferred) |
| **Backlog sync** | Initiative deferred — backlog row stays open; heroic-last `scheduleHint`. |

---

## Final todo

`backlog: sync ai_docs/backlog.md` — done by the orchestrator's Step 5 batch-boundary reconcile PR via `/close-backlog-rows` for each merged initiative; deferred initiatives leave their rows open.
