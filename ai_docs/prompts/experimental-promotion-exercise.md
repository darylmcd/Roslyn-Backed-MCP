# Experimental Feature Promotion Exercise

<!-- purpose: Focused prompt that exercises every experimental tool and prompt against a real workspace to produce the same promotion scorecard and coverage deliverable as deep-review-and-refactor.md, but without the full refactoring pass or exhaustive stable-surface audit. Output is agentic-optimized (machine-parseable tables, fixed schemas, promotion scorecards). -->
<!-- DO NOT DELETE THIS FILE.
     This is a companion to deep-review-and-refactor.md. It should be maintained
     whenever experimental tools are promoted or added. When the deep-review appendix
     updates, verify the experimental-only subset in this prompt still matches.
   Last surface audit: 2026-04-14 (catalog 2026.04; 29 experimental tools, 19 experimental prompts, 1 experimental resource after the v1.16.0 25-tool promotion batch). v1.15.0 added `apply_with_verify`, `remove_interface_member_preview`, and the `source_file_lines` resource template; v1.16.0 added `format_check` (experimental) and promoted 25 experimental tools to stable. Check the Phase sections below for updated counts. -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> **Primary purpose:** systematically exercise every experimental tool and prompt to produce an **experimental promotion scorecard** with evidence for promote / keep-experimental / needs-more-evidence / deprecate decisions. Also produces a minimal coverage ledger for the experimental surface.
>
> **Relationship to deep-review-and-refactor.md:** This prompt produces the same deliverable format (audit report with promotion scorecard), but is scoped exclusively to the experimental surface. Stable tools are exercised only as scaffolding (e.g. `workspace_load`, `compile_check`, `find_unused_symbols`) to set up scenarios for experimental tools. Run this prompt when you need promotion data without the full refactoring pass.

---

## Prompt

You are a senior .NET architect. You have **one mission:**

**Experimental promotion exercise** — For every experimental tool and prompt in the live catalog, exercise it end-to-end against a real C# workspace and score it against the promotion rubric. Record per-call evidence (correctness, schema accuracy, performance, error-path quality) into the mandatory audit file (see Output Format). Experimental entries you cannot exercise are marked `needs-more-evidence` with the reason.

**Known issues / prior findings:** If you have access to the server backlog at [`ai_docs/backlog.md`](../backlog.md), cross-check open MCP issues and cite matching ids. If no prior source exists, mark the regression section **N/A**.

**Phase order:** Run phases in this order: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11**.

**Portability and completeness contract:**

1. This prompt runs against **any loadable C# repo**. Prefer a `.sln` or `.slnx`; fall back to a `.csproj`.
   - If no entrypoint loads, continue with workspace-independent families (`analyze_snippet` wrappers, `evaluate_csharp` wrappers, prompts) and mark workspace-scoped entries `blocked`.
2. **Always run in `full-surface` mode** on a disposable branch/worktree/clone. Experimental tool promotion requires preview→apply round-trips that mutate workspace state. If disposable isolation is unavailable, switch to `conservative` and note that apply siblings are `skipped-safety` (which demotes them to `keep-experimental` or `needs-more-evidence` at best).
3. Treat `server_info`, `roslyn://server/catalog`, and `roslyn://server/resource-templates` as the **authoritative live surface**. If this prompt's appendix disagrees with the running server, the live catalog wins and the mismatch is a finding.
4. Build a **coverage ledger** scoped to the experimental surface. Every experimental tool and prompt must end with exactly one final status: `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, or `blocked`.
5. Record repo-shape constraints that affect exercisable scenarios: single vs multi-project, tests present, analyzers present, source generators present, DI usage, `.editorconfig`, Central Package Management, multi-targeting.
6. After every experimental tool/prompt call, record one line of promotion evidence: (a) correct result, (b) schema accurate, (c) error path actionable (on at least one negative probe), (d) preview→apply round-trip clean (where applicable), (e) `_meta.elapsedMs` within budget.

### Execution strategy and context conservation

1. **Subagent delegation encouraged** for heavy validation work (full-suite `test_run`, `test_coverage`, `build_workspace`). Keep experimental tool probes inline so promotion evidence is recorded per call.
2. **Phases run sequentially** — persist findings to the draft after every phase. Never start phase N+1 before phase N is persisted.
3. **Output budget per turn:** After cumulative tool-result size exceeds ~250 KB, persist and start a new turn.
4. **Workspace heartbeat between phases:** Call `workspace_list` before each phase. If the workspace is gone, reload from the recorded entrypoint.
5. **Always emit text per turn:** Every turn must emit at least one line of text describing what is being dispatched.

### Cross-cutting audit principles (apply to every call)

These rules apply to **every** experimental tool call. Capture findings in real time.

1. **Inline severity signal:** Tag each result: **PASS**, **FLAG**, or **FAIL**.
2. **Schema vs behavior:** Compare actual behavior against the MCP tool schema/description. Schema mismatches are high-value findings.
3. **Performance (`_meta.elapsedMs`):** Record per call. Budget: single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s.
4. **Error message quality:** When a tool returns an error, rate it: **actionable**, **vague**, or **unhelpful**.
5. **Parameter-path coverage:** For each experimental family, probe at least one non-default parameter path when the live schema exposes it.
6. **Precondition discipline:** Distinguish server defects from repo/environment constraints.

---

### Phase 0: Setup and live surface baseline

1. Pick the entrypoint (`.sln` / `.slnx` / `.csproj`).
2. Record `full-surface` or `conservative` mode and disposable isolation path.
3. Call `server_info` to capture server version, catalog version, live experimental counts.
4. Read `roslyn://server/catalog` to capture the authoritative live inventory.
5. Call `workspace_load` with the chosen entrypoint.
6. Call `workspace_status` to confirm clean load.
7. Call `project_graph` to understand project structure.
8. Run `dotnet restore <entrypoint>` to ensure packages are resolved.
9. Record repo-shape constraints (projects, tests, analyzers, DI, source generators, CPM, multi-targeting).
10. **Seed the coverage ledger** with one row per experimental tool and prompt from the live catalog. Pre-fill `tier=experimental`; leave `recommendation` blank until Phase 10.
11. **Seed the promotion scorecard** with one row per experimental tool and prompt.

**Checkpoint:** Did `server_info` and catalog agree? Did `workspace_load` succeed? Did `dotnet restore` complete? Is the ledger seeded with all experimental entries?

---

### Phase 1: Refactoring family — preview/apply pairs (10 experimental tools after v1.16.0 promotion batch)

Exercise each refactoring preview/apply pair. For each pair: preview → inspect diff → apply → `compile_check` → verify with a cross-tool probe → revert (if needed to keep workspace clean for the next pair).

#### 1a. Fix All (`fix_all_preview`, `fix_all_apply`)
1. Call `project_diagnostics` to find a diagnostic ID with multiple occurrences (e.g. IDE0005, CS8600).
2. Call `fix_all_preview` with `scope="solution"` and that diagnostic ID.
3. Verify the preview finds all instances. Probe a non-default scope (e.g. `scope="project"`).
4. Call `fix_all_apply` with the preview token.
5. Call `compile_check` to verify no regressions.
6. Call `project_diagnostics` again — did the count drop?

#### 1b. Format Range (`format_range_preview`, `format_range_apply`)
1. Pick a file with formatting issues.
2. Call `format_range_preview` with a specific line range.
3. Call `format_range_apply`.
4. Call `get_source_text` to verify only the specified range changed.

#### 1c. Extract Interface (`extract_interface_preview`, `extract_interface_apply`)
1. Pick a concrete type with public members.
2. Call `extract_interface_preview` with selected members.
3. Verify the generated interface in the preview.
4. Call `extract_interface_apply`.
5. Call `type_hierarchy` on the new interface — does it show the implementor?
6. Call `compile_check`.

#### 1d. Extract Type (`extract_type_preview`, `extract_type_apply`)
1. Call `get_cohesion_metrics` to find a type with LCOM4 > 1.
2. Call `find_shared_members` on that type.
3. Call `extract_type_preview` with an independent cluster's members.
4. Call `extract_type_apply`.
5. Call `compile_check`.

#### 1e. Move Type to File (`move_type_to_file_preview`, `move_type_to_file_apply`)
1. Find a file with multiple type declarations (or use a helper file).
2. Call `move_type_to_file_preview`.
3. Call `move_type_to_file_apply`.
4. Call `compile_check`.

#### 1f. Bulk Replace Type (`bulk_replace_type_preview`, `bulk_replace_type_apply`)
1. After extracting an interface (1c), call `bulk_replace_type_preview` to update consumer references from the concrete type to the interface.
2. Inspect the preview — are all parameter, field, and local usages found?
3. Call `bulk_replace_type_apply`.
4. Call `compile_check`.

#### 1g. Extract Method (`extract_method_preview`, `extract_method_apply`)
1. Call `get_complexity_metrics` to find a method with complexity ≥ 10 (or use `suggest_refactorings`).
2. Call `analyze_data_flow` on the target statement range.
3. Call `extract_method_preview` with the line range and a descriptive name.
4. Verify preview has correct parameters, return type, and call site.
5. Call `extract_method_apply`.
6. Call `find_references` on the new method — at least one call site?
7. Call `compile_check`.

**Checkpoint:** All 14 refactoring tools exercised? Each preview/apply pair round-tripped cleanly? `compile_check` passed after each apply? At least one non-default parameter path probed per family?

---

### Phase 2: File operations family (3 experimental tools after v1.16.0 promotion batch — apply siblings only)

Exercise each file operation preview/apply pair on a disposable checkout. Clean up after each to keep the workspace stable.

#### 2a. Create File (`create_file_preview`, `create_file_apply`)
1. Call `create_file_preview` to create a new source file in a project.
2. Verify the preview shows correct namespace and content.
3. Call `create_file_apply`.
4. Call `compile_check`.

#### 2b. Move File (`move_file_preview`, `move_file_apply`)
1. Call `move_file_preview` on the file just created, moving it to a different directory with namespace update.
2. Verify the preview shows the path change and namespace update.
3. Call `move_file_apply`.
4. Call `compile_check`.

#### 2c. Delete File (`delete_file_preview`, `delete_file_apply`)
1. Call `delete_file_preview` on the file created in 2a (now moved in 2b).
2. Verify the preview.
3. Call `delete_file_apply`.
4. Call `compile_check`.

**Checkpoint:** All 6 file operation tools exercised? Create → move → delete lifecycle completed cleanly? Namespace updates correct in move preview?

---

### Phase 3: Project mutation family (2 experimental tools after v1.16.0 promotion batch — `add_central_package_version_preview` + `apply_project_mutation`)

Exercise each project mutation preview, then apply at least one reversible mutation round-trip.

#### 3a. Package references
1. Call `add_package_reference_preview` — add a NuGet package (e.g. `Newtonsoft.Json`).
2. Call `remove_package_reference_preview` — remove the same package.
3. Apply the add via `apply_project_mutation`, then apply the remove to reverse it. Verify with `workspace_reload` + `compile_check`.

#### 3b. Project references
1. If multi-project, call `add_project_reference_preview` to add a project-to-project reference.
2. Call `remove_project_reference_preview` to remove it.
3. Apply and reverse if multi-project; otherwise `skipped-repo-shape`.

#### 3c. Project properties
1. Call `set_project_property_preview` — set a property (e.g. `Nullable=enable`).
2. Call `set_conditional_property_preview` — set a conditional property scoped to a configuration.
3. Apply and reverse one property change.

#### 3d. Target frameworks
1. Call `add_target_framework_preview` — add a target framework.
2. Call `remove_target_framework_preview` — remove it.
3. Apply and reverse one framework mutation if safe.

#### 3e. Central Package Management
1. If the repo uses `Directory.Packages.props`, call `add_central_package_version_preview` and `remove_central_package_version_preview`.
2. Otherwise `skipped-repo-shape`.

#### 3f. MSBuild properties dump
1. Call `get_msbuild_properties` with default parameters.
2. Call `get_msbuild_properties` with `propertyNameFilter` or `includedNames` to probe a non-default path.
3. Compare with `evaluate_msbuild_property` on a known key — do values match?

**Checkpoint:** All 12 project-mutation tools exercised or marked `skipped-repo-shape`? At least one forward-reverse apply round-trip completed? `get_msbuild_properties` non-default path probed?

---

### Phase 4: Scaffolding family (3 experimental tools after v1.16.0 promotion batch — `scaffold_test_preview` promoted)

#### 4a. Scaffold Type (`scaffold_type_preview`, `scaffold_type_apply`)
1. Call `scaffold_type_preview` to scaffold a new class. Verify `internal sealed class` default (v1.8+).
2. Call `scaffold_type_apply`.
3. Call `compile_check`.
4. Clean up (delete the scaffolded file or leave for later deletion).

#### 4b. Scaffold Test (`scaffold_test_preview`, `scaffold_test_apply`)
1. If the repo has a test project, call `scaffold_test_preview` for an existing type. Verify framework detection and constructor expressions.
2. Call `scaffold_test_apply`.
3. Call `test_discover` to verify the new test is discoverable.
4. Call `compile_check`.

**Checkpoint:** Both scaffolding pairs exercised? Namespace inference correct? Test framework auto-detected?

---

### Phase 5: Dead code, editing, configuration, undo (5 experimental tools after v1.16.0 promotion batch — `apply_text_edit`, `add_pragma_suppression`, `set_editorconfig_option`, `set_diagnostic_severity`, and `revert_last_apply` promoted)

#### 5a. Dead code removal (`remove_dead_code_preview`, `remove_dead_code_apply`)
1. Call `find_unused_symbols` to find confirmed dead symbols.
2. Call `remove_dead_code_preview` with their symbol handles.
3. Call `remove_dead_code_apply`.
4. Call `compile_check`.

#### 5b. Direct text edits (`apply_text_edit`, `apply_multi_file_edit`)
1. Call `apply_text_edit` with a small targeted edit to a single file.
2. Call `apply_multi_file_edit` with coordinated edits across two files.
3. Call `compile_check`.

#### 5c. Pragma suppression (`add_pragma_suppression`)
1. Find a diagnostic occurrence via `project_diagnostics`.
2. Call `add_pragma_suppression` to insert `#pragma warning disable` before the line.
3. Call `get_source_text` to verify the pragma is present.
4. Call `compile_check`.

#### 5d. Configuration writers (`set_editorconfig_option`, `set_diagnostic_severity`)
1. Call `set_editorconfig_option` to set a benign key (e.g. `dotnet_sort_system_directives_first = true`).
2. Call `get_editorconfig_options` to verify the change.
3. Call `set_diagnostic_severity` to set a diagnostic severity (e.g. `dotnet_diagnostic.IDE0005.severity = suggestion`).
4. Verify the `.editorconfig` reflects the change.

#### 5e. Undo (`revert_last_apply`, `apply_with_verify`)
1. Call `revert_last_apply` to revert the most recent Roslyn apply.
2. Call `compile_check` to verify workspace consistency.
3. Call `revert_last_apply` again — verify it returns a clear "nothing to revert" message (negative probe).
4. Exercise `apply_with_verify` (v1.15+ atomic apply → compile_check → auto-revert): produce a known-good preview via `organize_usings_preview`, pass its token to `apply_with_verify(previewToken, rollbackOnError: true)`, assert `status=applied`. Then produce a known-bad preview (e.g. `extract_method_preview` over a region that reassigns an existing local on an older extract path) — on post-v1.15 repos the fix landed and this yields `status=applied`; on upstream regressions expect `status=rolled_back` with `introducedErrors` populated.

#### 5f. Composite dead interface removal (`remove_interface_member_preview`)
v1.15+ composite that wraps `find_unused_symbols` → `find_implementations` → `remove_dead_code_preview` in one call.
1. Find a dead interface member via `find_unused_symbols` (filter to interface members if the repo has any).
2. Call `remove_interface_member_preview(workspaceId, interfaceMemberHandle)` with its handle. Assert the response includes `previewToken`, `status=previewed`, `implementationCount`, and the list of implementations. If the response is `status=refused` with `externalCallers` populated, the member isn't dead — pick another.
3. Apply via `remove_dead_code_apply` with the returned preview token and verify compilation.

**Checkpoint:** All 13 tools exercised (11 legacy + `apply_with_verify` + `remove_interface_member_preview`)? Text edit undo worked? Configuration writes verified? `revert_last_apply` negative path tested? Composite dead interface removal atomically removed the interface member + every implementation?

---

### Phase 6: Cross-project refactoring family (3 experimental tools)

These are preview-only tools (no apply counterparts).

1. If multi-project workspace:
   a. Call `move_type_to_project_preview` to preview moving a type to another project.
   b. Call `extract_interface_cross_project_preview` to extract an interface into a different project.
   c. Call `dependency_inversion_preview` to preview extracting an interface and updating constructor dependencies.
2. If single-project: mark all three `skipped-repo-shape`.
3. For each preview, verify the diff includes correct namespace updates and project reference additions.

**Checkpoint:** All 3 cross-project tools exercised or skipped-repo-shape?

---

### Phase 7: Orchestration family (4 experimental tools)

1. Call `migrate_package_preview` — pick a package to migrate (e.g. old version → new version, or package A → package B).
2. Call `split_class_preview` — pick a type that could be split into partial class files.
3. Call `extract_and_wire_interface_preview` — if DI registrations exist, preview extracting an interface and rewriting DI wiring.
4. If applicable, call `apply_composite_preview` to apply one orchestration result. This is a **destructive apply** despite the name — only use in `full-surface` mode.
5. Call `compile_check` after any apply.

**Checkpoint:** All 4 orchestration tools exercised or marked with a reason? `apply_composite_preview` exercised or `skipped-safety`?

---

### Phase 7b: Experimental resources (1 template)

v1.15+ ships one experimental resource template:

1. `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}` — inclusive 1-based line-range slice over `source_file`. Exercise by requesting a known small range (e.g. `lines/1-10`) on a file in the workspace; verify the response is prefixed with a `// roslyn://…/lines/N-M of T` marker comment and that the body is only the requested lines. Exercise an invalid range (`lines/10-5`) and verify it returns a clear error (not a hang).

**Checkpoint:** Line-range marker comment present? Invalid range rejected with a structured error?

---

### Phase 8: Prompt verification (19 experimental prompts)

All prompts are experimental. For each:
1. **Schema sanity** — look up the prompt in `roslyn://server/catalog` and verify argument list.
2. **Rendered output correctness** — invoke with realistic arguments from prior phases. Verify rendered text references real tool names.
3. **Actionability** — does it produce concrete guidance with tool names and workflow steps?
4. **Hallucinated tools** — count any references to tools not in the live catalog.
5. **Idempotency** — call the same prompt twice; rendered text should be stable.

Exercise all 19 prompts:

| Prompt | Realistic input source |
|--------|----------------------|
| `explain_error` | Diagnostic ID from Phase 1 or 5 |
| `suggest_refactoring` | Symbol from Phase 1 |
| `review_file` | File modified in Phase 1 |
| `analyze_dependencies` | Project from workspace |
| `debug_test_failure` | Test name (or synthetic if no failures) |
| `refactor_and_validate` | Symbol handle from Phase 1 |
| `fix_all_diagnostics` | Diagnostic ID from Phase 1 |
| `guided_package_migration` | Package from Phase 3 |
| `guided_extract_interface` | Type from Phase 1c |
| `security_review` | Workspace ID |
| `discover_capabilities` | Task category (e.g. "refactoring") |
| `dead_code_audit` | Workspace ID |
| `review_test_coverage` | Workspace/project |
| `review_complexity` | Workspace/project |
| `cohesion_analysis` | Type with LCOM4 > 1 |
| `consumer_impact` | Type from Phase 1 |
| `guided_extract_method` | Method from Phase 1g |
| `msbuild_inspection` | Project from Phase 3 |
| `session_undo` | Workspace ID |

For every prompt, append one row to the **Prompt verification** table.

**Checkpoint:** All 19 prompts exercised or blocked? Schema matched? No hallucinated tool names? Rendered output actionable?

---

### Phase 9: Negative probes (error paths for experimental tools)

For each experimental family, probe at least one error path. The goal is to verify actionable error messages.

#### 9a. Stale and invalid tokens
1. Call any `*_apply` tool with a fabricated or already-consumed preview token. Verify actionable error.
2. Create a fresh preview, mutate the workspace, then try to apply the stale token. Verify version-mismatch rejection.

#### 9b. Invalid inputs
1. Call `apply_text_edit` with out-of-range line numbers. Verify error.
2. Call `remove_dead_code_preview` with a fabricated symbol handle. Verify error.
3. Call `scaffold_type_preview` with an invalid type name (e.g. `"2foo"`). Verify error.
4. Call `extract_method_preview` with `startLine > endLine`. Verify error.
5. Call `set_editorconfig_option` with an empty key. Verify error.

#### 9c. Precondition failures
1. Call `add_central_package_version_preview` on a repo without `Directory.Packages.props`. Verify actionable error.
2. Call `scaffold_test_preview` on a project with no test framework. Verify error.
3. Call `revert_last_apply` when undo stack is empty. Verify clear message.

For each negative probe, record the error message quality: **actionable**, **vague**, or **unhelpful**.

**Checkpoint:** At least one negative probe per major experimental family? Error messages rated?

---

### Phase 10: Final surface closure and scorecard computation

1. Compare the coverage ledger against the live catalog's experimental entries.
2. For every unaccounted experimental tool or prompt, either call it now or assign a final explicit status with a reason.
3. Confirm all audit-only mutations were reverted or cleaned up.
4. **Compute the promotion scorecard.** For each experimental tool/prompt, compute one recommendation:

   - **`promote`** — ALL of: (a) exercised end-to-end with at least one non-default parameter path, (b) schema matched behavior on every probe, (c) no FAIL findings in this run or prior backlog, (d) p50 `elapsedMs` within budget, (e) preview/apply round-trip clean (if applicable), (f) error path produced actionable message on at least one negative probe, (g) catalog description matched actual behavior.
   - **`keep-experimental`** — exercised with pass signal but missing at least one promote criterion (typically: apply round-trip not performed, or non-default path not probed, or error path not tested).
   - **`needs-more-evidence`** — not exercised (`skipped-repo-shape`, `skipped-safety`, `blocked`) or too shallow to judge.
   - **`deprecate`** — exercised with FAIL findings that warrant removal.

5. Confirm all sections of the output format are populated.

---

### Phase 11: Regression check against known issues

1. Read `ai_docs/backlog.md` (or available prior source) and select 3–5 items that relate to experimental tools.
2. Reproduce each scenario. Record: **still reproduces**, **partially fixed**, or **candidate for closure**.
3. Include in the report's Known issue regression check section.

---

## Output Format — MANDATORY

### Where to save the report

**Canonical path:** `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/<timestamp>_<repo-id>_experimental-promotion.md`

- `<timestamp>` is UTC `yyyyMMddTHHmmssZ`.
- Same naming convention as deep-review audit files.
- Create `ai_docs/audit-reports/` if missing.

### Draft persistence

Maintain a draft at `<canonical-path>.draft.md` and append after every phase. Rename to canonical name when complete.

### Report contents (required sections)

| # | Section | Purpose |
|---|---------|---------|
| 1 | Header | Run metadata — server, client, mode, scale, repo shape |
| 2 | Coverage ledger (experimental only) | One row per experimental tool/prompt |
| 3 | Performance baseline | `_meta.elapsedMs` per exercised experimental tool |
| 4 | Schema vs behaviour drift | Mismatches observed |
| 5 | Error message quality | Negative-path probe ratings |
| 6 | Parameter-path coverage | Non-default paths tested per family |
| 7 | Prompt verification | Phase 8 per-prompt table |
| 8 | Experimental promotion scorecard | Per-entry recommendation with evidence |
| 9 | MCP server issues (bugs) | Per-issue detail |
| 10 | Improvement suggestions | UX friction, workflow gaps |
| 11 | Known issue regression check | Phase 11 results |

### Markdown template

```markdown
# Experimental Promotion Exercise Report

## 1. Header
- **Date:**
- **Audited solution:**
- **Audited revision:** (commit / branch if available)
- **Entrypoint loaded:**
- **Audit mode:** (`full-surface` / `conservative` — state reason)
- **Isolation:** (disposable branch/worktree/clone path, or conservative rationale)
- **Client:** (name/version; note if prompt invocation is client-blocked)
- **Workspace id:**
- **Server:** (from `server_info`)
- **Catalog version:**
- **Experimental surface:** N experimental tools, M experimental prompts
- **Scale:** ~N projects, ~M documents
- **Repo shape:**
- **Prior issue source:**

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised | 1a | | |
| tool | `fix_all_apply` | refactoring | exercised-apply | 1a | | |
| tool | `create_file_preview` | file-operations | exercised | 2a | | |
| prompt | `explain_error` | prompts | exercised | 8 | | |
| … | | | | | | |

## 3. Performance baseline (`_meta.elapsedMs`)

> One row per exercised experimental tool. Budget: reads ≤5 s, scans ≤15 s, writers ≤30 s.

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| | | | | | | | |

## 4. Schema vs behaviour drift

> Empty table → `No schema/behaviour drift observed.`

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| | | | | | |

## 5. Error message quality

> Populated from Phase 9 negative probes.

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| | | | | |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project | | |
| `scaffold_type_preview` | kind=record / kind=interface | | |
| `get_msbuild_properties` | propertyNameFilter | | |
| File operations | move with namespace update | | |
| Project mutation | conditional property, CPM | | |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | | | | | | | |
| `suggest_refactoring` | | | | | | | |
| `review_file` | | | | | | | |
| `analyze_dependencies` | | | | | | | |
| `debug_test_failure` | | | | | | | |
| `refactor_and_validate` | | | | | | | |
| `fix_all_diagnostics` | | | | | | | |
| `guided_package_migration` | | | | | | | |
| `guided_extract_interface` | | | | | | | |
| `security_review` | | | | | | | |
| `discover_capabilities` | | | | | | | |
| `dead_code_audit` | | | | | | | |
| `review_test_coverage` | | | | | | | |
| `review_complexity` | | | | | | | |
| `cohesion_analysis` | | | | | | | |
| `consumer_impact` | | | | | | | |
| `guided_extract_method` | | | | | | | |
| `msbuild_inspection` | | | | | | | |
| `session_undo` | | | | | | | |

## 8. Experimental promotion scorecard

> One row per experimental entry. Rubric in Phase 10.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | | | | n/a | | | |
| tool | `fix_all_apply` | refactoring | exercised-apply | | | | yes | | | |
| tool | `create_file_preview` | file-operations | exercised | | | | n/a | | | |
| tool | `create_file_apply` | file-operations | exercised-apply | | | | yes | | | |
| prompt | `explain_error` | prompts | exercised | | | n/a | n/a | | | |
| … | | | | | | | | | | |

## 9. MCP server issues (bugs)

### 9.1 (title or tool name)
| Field | Detail |
|--------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |

(repeat per issue, or write `**No new issues found**`)

## 10. Improvement suggestions
- `tool_name` — suggestion
- …

## 11. Known issue regression check

> `**N/A — no prior source**` when no backlog/audit source existed.

| Source id | Summary | Status |
|-----------|---------|--------|
| backlog-id | one-line summary | still reproduces / partially fixed / candidate for closure |
```

### Completion gate

The file must exist at the canonical path. The task is **incomplete** without this file.

---

## Appendix — Experimental surface reference (2026.04)

> **Maintenance note:** This appendix mirrors the experimental entries from the live catalog. The live catalog from Phase 0 is authoritative. Treat this as a convenience snapshot.
> Last verified: 2026-04-14 against catalog version 2026.04 — **29 experimental tools, 19 experimental prompts** (v1.16.0 25-tool promotion batch + new `format_check`).

### Experimental tools by category

#### `refactoring` (10 experimental — `format_range_preview`, `extract_type_preview`, `move_type_to_file_preview`, `bulk_replace_type_preview`, `extract_method_preview` promoted in v1.16.0)
| Tool | RO/D | Pair | Phase |
|------|------|------|-------|
| `fix_all_preview` | RO | — | 1a |
| `fix_all_apply` | D | `fix_all_preview` | 1a |
| `format_range_apply` | D | `format_range_preview` (now stable) | 1b |
| `extract_interface_preview` | RO | — | 1c |
| `extract_interface_apply` | D | `extract_interface_preview` | 1c |
| `extract_type_apply` | D | `extract_type_preview` (now stable) | 1d |
| `move_type_to_file_apply` | D | `move_type_to_file_preview` (now stable) | 1e |
| `bulk_replace_type_apply` | D | `bulk_replace_type_preview` (now stable) | 1f |
| `extract_method_apply` | D | `extract_method_preview` (now stable) | 1g |
| `format_check` | RO | — | 1h (new v1.16.0; solution-wide format verification) |

#### `file-operations` (3 experimental — all `*_preview` siblings promoted in v1.16.0)
| Tool | RO/D | Pair | Phase |
|------|------|------|-------|
| `create_file_apply` | D | `create_file_preview` (now stable) | 2a |
| `move_file_apply` | D | `move_file_preview` (now stable) | 2b |
| `delete_file_apply` | D | `delete_file_preview` (now stable) | 2c |

#### `project-mutation` (2 experimental — `add_central_package_version_preview` and `apply_project_mutation` only after v1.16.0 promotion batch)
| Tool | RO/D | Phase |
|------|------|-------|
| `add_central_package_version_preview` | RO | 3e |
| `apply_project_mutation` | D | 3a–3d |

#### `scaffolding` (3 experimental — `scaffold_test_preview` promoted in v1.16.0)
| Tool | RO/D | Pair | Phase |
|------|------|------|-------|
| `scaffold_type_preview` | RO | — | 4a |
| `scaffold_type_apply` | D | `scaffold_type_preview` | 4a |
| `scaffold_test_apply` | D | `scaffold_test_preview` (now stable) | 4b |

#### `dead-code` (2 experimental — `remove_dead_code_preview` promoted in v1.16.0; `remove_interface_member_preview` added v1.15.0)
| Tool | RO/D | Pair | Phase |
|------|------|------|-------|
| `remove_dead_code_apply` | D | `remove_dead_code_preview` (now stable) | 5a |
| `remove_interface_member_preview` | RO | — | 5a (v1.15.0; preview-only sweep for unused interface members) |

#### `editing` (1 experimental — `apply_text_edit` and `add_pragma_suppression` promoted in v1.16.0)
| Tool | RO/D | Phase |
|------|------|-------|
| `apply_multi_file_edit` | D | 5b |

#### `configuration` (0 experimental — all promoted in v1.16.0)

The configuration family is fully stable as of v1.16.0; no experimental rows remain. Skip Phase 5d unless the catalog reintroduces an experimental configuration tool.

#### `undo` (1 experimental — `revert_last_apply` promoted in v1.16.0; `apply_with_verify` added v1.15.0)
| Tool | RO/D | Phase |
|------|------|-------|
| `apply_with_verify` | D | 5e (v1.15.0; apply preview + auto-verify via compile_check, auto-revert on new errors) |

#### `cross-project-refactoring` (3 experimental)
| Tool | RO/D | Phase |
|------|------|-------|
| `move_type_to_project_preview` | RO | 6 |
| `extract_interface_cross_project_preview` | RO | 6 |
| `dependency_inversion_preview` | RO | 6 |

#### `orchestration` (4 experimental)
| Tool | RO/D | Phase |
|------|------|-------|
| `migrate_package_preview` | RO | 7 |
| `split_class_preview` | RO | 7 |
| `extract_and_wire_interface_preview` | RO | 7 |
| `apply_composite_preview` | D | 7 |

### Experimental prompts (19)

| Prompt | Phase |
|--------|-------|
| `explain_error` | 8 |
| `suggest_refactoring` | 8 |
| `review_file` | 8 |
| `analyze_dependencies` | 8 |
| `debug_test_failure` | 8 |
| `refactor_and_validate` | 8 |
| `fix_all_diagnostics` | 8 |
| `guided_package_migration` | 8 |
| `guided_extract_interface` | 8 |
| `security_review` | 8 |
| `discover_capabilities` | 8 |
| `dead_code_audit` | 8 |
| `review_test_coverage` | 8 |
| `review_complexity` | 8 |
| `cohesion_analysis` | 8 |
| `consumer_impact` | 8 |
| `guided_extract_method` | 8 |
| `msbuild_inspection` | 8 |
| `session_undo` | 8 |

### Promotion checklist (when executing a promotion after this exercise)

1. Confirm tests + coverage for the tool's service path.
2. Update `ServerSurfaceCatalog` entry from `"experimental"` to `"stable"`.
3. Update `docs/product-contract.md` stable tool families list.
4. Update `docs/experimental-promotion-analysis.md` with the promoted batch and evidence.
5. Minor semver bump; `CHANGELOG.md` **Added** under stable surface.
6. Run `./eng/verify-release.ps1` and `./eng/verify-ai-docs.ps1`.
7. Re-run catalog parity: `SurfaceCatalogTests`.
8. Update this prompt's appendix counts and `deep-review-and-refactor.md` appendix.
