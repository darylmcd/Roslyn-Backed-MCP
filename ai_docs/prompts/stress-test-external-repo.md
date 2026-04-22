# Stress Test: External Repo Performance Baseline

<!-- purpose: Run against a large external C# repo to capture tool-level performance baselines, identify bottlenecks, and stress recovery paths. Results are written back to the Roslyn-Backed-MCP repo for consumption by the development team. -->

> **This prompt is a null-op without the Roslyn MCP server.** If `mcp__roslyn__server_info` is not callable in your current tool list, stop and ask the user to start the server. Phase -1 verifies this as a hard gate.

> Use this prompt with an AI coding agent that has an MCP client connected to the Roslyn MCP server.
> **Run from the external repo's directory** (e.g., `C:\Code-Repo\jellyfin`), not from the Roslyn-Backed-MCP repo.
> All findings are written to `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/` using the standard naming convention. If you can't resolve `<Roslyn-Backed-MCP-root>`, fall back to the current workspace root under `ai_docs/audit-reports/` and note the intended copy destination in the header.

---

## Prompt

You are a performance engineer stress-testing the Roslyn MCP server against a large external C# codebase. Your mission: exercise every major tool category under realistic scale, capture timing baselines, identify bottlenecks, and test recovery paths.

**Output destination.** Write all results to `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/`. Naming:

```
{YYYYMMDD}T{HHMMSS}Z_{repo-name}_stress-test.md
```

Example: `20260422T220000Z_jellyfin_stress-test.md`

If the file grows past ~30 KB, split: `..._stress-test-part1.md` (phases 1–4), `..._stress-test-part2.md` (phases 5–8).

**Persist after every phase.** Append incrementally — do not wait until the end.

---

## Output format

Header schema:

```markdown
# Stress Test Report: {Repo Name}

| Field | Value |
|---|---|
| Repo | {org/repo} |
| Solution | {path to .sln} |
| Projects | {count} |
| Documents | {count} |
| Target frameworks | {list} |
| Workspace load (ms) | {from _meta.elapsedMs} |
| Warm-up (ms) | {from workspace_warm _meta.elapsedMs, or "skipped"} |
| projectsWarmed / coldCompilationCount | {from workspace_warm} |
| Report generated | {UTC timestamp} |
| Roslyn MCP version | {from server_info} |
| Catalog version | {from server_info.catalogVersion} |
| Live surface | tools: {stable}/{experimental}, resources: {stable}/{experimental}, prompts: {stable}/{experimental} |
```

Per-call table row format:

```markdown
| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 1 | compile_check | full solution | 10440 | PASS (0 errors) | baseline |
| 2 | find_references | BaseItem | 3200 | PASS (147 refs) | high-fan symbol |
```

Tag each result: **PASS**, **SLOW** (>2× budget), **TIMEOUT**, **ERROR**, **UNEXPECTED**.

Performance budgets:
- Single-symbol lookups (`go_to_definition`, `symbol_info`, `enclosing_symbol`): ≤2 s
- Search / scan (`find_references`, `symbol_search`, `semantic_search`): ≤10 s
- Solution-wide analysis (`compile_check`, complexity, cohesion, dead code): ≤30 s
- Mutation previews (rename, extract, code fix): ≤15 s
- Build / test: ≤120 s

---

## Phase -1: MCP server precondition (MUST run first, hard gate)

This prompt exercises the Roslyn MCP server end-to-end. Without it, nothing below is meaningful.

1. **Check the tool list.** Verify `mcp__roslyn__server_info` appears in your current tool surface. If not, STOP and tell the user:

   > *"This prompt requires the Roslyn MCP server (mcp__roslyn__* tools must be callable). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in `.mcp.json` / `settings.json` — confirm `mcp__roslyn__server_info` is available, then rerun."*

   Do **not** substitute `Bash: dotnet build`, `Read`, `Grep`, or other host-side fallbacks. The entire point of this run is MCP-server performance; a run without the server produces no baseline.

2. **Call `server_info`.** Record `version`, `catalogVersion`, `runtime`, `os`, `surface.*` counts, `surface.registered.parityOk`, and `connection.state`. If `state != "ready"`, call `server_heartbeat` once. If it never becomes `ready`, halt.

3. **Sanity-check the catalog resource.** Read `roslyn://server/catalog`; compare per-category counts against `server_info.surface`. A mismatch here is a P2 finding.

**Hard-gate checkpoint:** `server_info` callable? `connection.state == "ready"`? `parityOk == true`? Catalog resource matches `server_info`? Any `no` is a halt, not a silent proceed.

---

## Phase 0: Setup

1. `workspace_load(path=<sln>, verbose=false)`. Record `_meta.elapsedMs`.
2. `workspace_health` — record `isReady`, error counts, `restoreHint`.
3. **`workspace_warm(workspaceId)` (v1.28+).** Prime `GetCompilationAsync` + semantic models so downstream timings are cache-hit-dominated and reproducible. Record `projectsWarmed`, `coldCompilationCount`, `elapsedMs`. This is the recommended post-load step for every run; state `warm-up: yes` in the header.
4. `project_graph` — project count, dependency depth.
5. If `restoreHint` is non-null, run `dotnet restore` and reload. If the host cannot shell out, record the limitation in the header.

**Persist the report header and Phase 0 results now.**

---

## Phase 1: Compilation baseline

1. `compile_check` — full solution, `severity=Error`. Record error count and timing.
2. `compile_check` — full solution, `severity=Warning, limit=50`. Record warning count.
3. `compile_check` with `emitValidation=true` — compare timing to step 1.
4. `project_diagnostics(limit=50, severity=Warning)` — record total counts.
5. Pick the **largest project** (by document count from `workspace_load`). `compile_check` filtered to that project.
6. `format_check` (solution-wide) — record `violationCount` and timing.

**Persist Phase 1 results.**

---

## Phase 2: Symbol resolution at scale

Pick **3 symbols** with high expected fan-out. Good candidates: a base class used across many projects, a widely-used interface, a utility method. For each:

1. `symbol_search` — substring match. Record hit count and timing.
2. `symbol_info` — full metadata. Record timing.
3. **Cold vs warm `symbol_search` delta** (first symbol only, first call of the run): run `symbol_search` once immediately after `workspace_load` without `workspace_warm`, then run `workspace_warm` and repeat — record the delta. Skip this sub-step if warm-up was already done in Phase 0 (record `skipped — warmup done in Phase 0`). This measures whether `workspace_warm` is doing its job.
4. `find_references` — record reference count and timing. If >100 refs, note pagination.
5. `find_references(offset=50, limit=50)` — page 2. Record timing.
6. `callers_callees` — caller/callee counts and timing.
7. `impact_analysis` — affected file count and timing.

Also:
8. `symbol_search` with a **very broad query** (e.g. `"Get"`) — hit count and timing.
9. `semantic_search("async methods returning Task<bool>")` — timing and result quality. Repeat with a paraphrase and compare.

**Persist Phase 2 results.**

---

## Phase 3: Navigation and type hierarchy

Pick a class with a deep hierarchy or many implementations:

1. `type_hierarchy` — depth, derived type count, timing.
2. `find_implementations` — for an interface, count + timing.
3. `go_to_definition` on 5 different symbols across different projects.
4. `document_symbols` on the **largest file**. Record symbol count and timing.
5. `get_syntax_tree` on the same large file, `maxDepth=5`. Record timing and whether output truncated.
6. `member_hierarchy` on a virtual/override method.
7. `probe_position` at a known cursor on the same file — timing.

**Persist Phase 3 results.**

---

## Phase 4: Solution-wide analysis tools

Run each against the full solution (or largest project where solution-wide is too slow):

1. `get_complexity_metrics(limit=20, minComplexity=10)` — timing, top hotspot.
2. `get_cohesion_metrics(limit=20)` — timing, worst LCOM4.
3. `find_unused_symbols(limit=20)` — timing, count.
4. `find_duplicated_methods` — timing, top-N cluster size.
5. `find_duplicate_helpers` — timing; note false-positive ratio on BCL wrappers (backlog `find-duplicate-helpers-framework-wrapper-false-positive`).
6. `suggest_refactorings(limit=10)` — timing, suggestion count.
7. `get_namespace_dependencies(circularOnly=true)` — timing, cycle count.
8. `get_di_registrations` — timing, registration count.
9. `get_nuget_dependencies` — timing, package count.
10. `list_analyzers(limit=50)` — analyzer count, timing.

### Phase 4b: v1.17–v1.28 composites

Composites dispatch multiple sub-queries internally; record end-to-end timing vs the sum of their constituents.

11. `symbol_impact_sweep` — pick a high-fan-out symbol from Phase 2. Composite of references + switch-exhaustiveness diagnostics + mapper callsites. Compare `_meta.elapsedMs` against `find_references` + `project_diagnostics` separately.
12. `test_reference_map(projectName?)` — if tests present. Record timing and `coveragePercent`. Compare against `test_coverage` duration.
13. `validate_workspace(runTests=false)` — compare end-to-end against the sum of `compile_check` + `project_diagnostics(severity=Error)` + `test_related_files`. Budget: composite ≤1.2× sum of parts.
14. `validate_recent_git_changes` — if the external repo has git metadata. Record timing and the reported status (clean / compile-error / analyzer-error / test-failure).
15. `trace_exception_flow` on a `throw` site in a hot file. Record timing and whether the response walks to a `catch` or reports unhandled-at-boundary.

**Persist Phase 4 results.**

---

## Phase 5: Mutation previews (read-only stress)

**Do NOT apply any mutations.** Preview only — this is someone else's code.

Pick targets from Phase 2/3 findings:

1. `rename_preview` — symbol with 20+ references.
2. `rename_preview` — symbol with 100+ references (if available).
3. `extract_interface_preview` — concrete class with 5+ public methods.
4. `extract_method_preview` — 20+ line block inside a complex method (use Phase 4 hotspot).
5. `code_fix_preview` — a diagnostic from Phase 1.
6. `fix_all_preview` — most common warning, `scope="project"`.
7. `format_document_preview` — largest file.
8. `organize_usings_preview` — 3 different files, record average.

### Phase 5b: v1.17–v1.28 previews

9. `preview_multi_file_edit` — simulate a small cross-file edit spanning 3+ files. Compare timing against `apply_text_edit` serially on the same files (preview should be faster — single-snapshot validation).
10. `restructure_preview` — run a simple pattern (e.g. `__x__?.ToString() ?? ""` → `__x__ as string ?? ""`) project-wide. Match count and timing.
11. `change_signature_preview` — pick a method with 10+ callsites, `op=add` with a default value. Record timing.
12. `symbol_refactor_preview` — compose 3 operations (rename + edit + restructure). Timing; confirm the 25-op / 500-file cap.
13. `replace_invocation_preview` — a method name with many callsites. Timing and affected-file count.
14. `change_type_namespace_preview` — a type in a leaf namespace. Timing.
15. `preview_record_field_addition` and/or `record_field_add_with_satellites_preview` — if record types are prevalent. Otherwise `skipped-repo-shape`.
16. `extract_shared_expression_to_helper_preview` — find a duplicated expression cluster via Phase 4 and preview extraction. Timing.

**Persist Phase 5 results.**

---

## Phase 6: Throughput and rapid-fire calls

Sequential throughput — back-to-back calls:

1. `symbol_info` **10 times** on different symbols. Record per-call timing; note any degradation.
2. `go_to_definition` **10 times** on different symbols.
3. `enclosing_symbol` **10 times** at random file positions.
4. Compute min, max, mean, p95 for each batch.

**Persist Phase 6 results.**

---

## Phase 7: Recovery and edge cases

1. **Invalid symbol position.** `symbol_info` at whitespace.
2. **Out-of-bounds position.** `go_to_definition` with `line=999999`.
3. **Nonexistent file.** `document_symbols` with a fabricated path.
4. **Stale workspace.** `workspace_reload`, immediately followed by `compile_check`.
5. **Double load.** `workspace_load` again with the same path — verify idempotent (same `workspaceId`).
6. **Large result set.** `find_references` on the most-referenced symbol, `limit=500`. Timing and pagination.
7. **Empty search.** `symbol_search` with a gibberish query.
8. **Fabricated symbol handle.** `find_references` with a junk handle — expect `category: NotFound`, not silent empty.

Rate each error response: **actionable**, **vague**, or **unhelpful**.

**Persist Phase 7 results.**

---

## Phase 8: Summary and recommendations

Final section:

1. **Performance summary table** — min/max/mean/p95 per tool category (lookup, search, analysis, mutation-preview).
2. **Bottleneck list** — tools that exceeded budget, sorted by severity.
3. **Scalability assessment** — "would this tool survive a 100-project solution?" rating per category.
4. **Warm-up impact** — cold vs warm `symbol_search` delta from Phase 2.3 (or explicitly `skipped`).
5. **Error quality scorecard** — percentage of error responses rated actionable.
6. **Top 5 recommendations** — specific, actionable improvements ranked by impact.
7. **Comparison baseline** — if prior stress-test reports exist in the audit-reports folder, note improvements or regressions.

**Persist Phase 8 results. Report is complete.**

---

## Execution rules

1. **Never apply mutations.** Preview only. This is someone else's code.
2. **Persist after every phase.** Write incrementally to the output file.
3. **Record `_meta.elapsedMs` for every tool call.** This is the primary deliverable.
4. **If a tool times out or errors, record it and move on.** Do not retry more than once.
5. **Workspace heartbeat.** Call `workspace_health` (or `server_heartbeat`) between phases. If the workspace is gone, reload and note the incident.
6. **Context conservation.** If cumulative tool output exceeds ~200 KB, start a new turn. Summarize rather than paste raw output.
7. **Always emit text per turn.** Describe what you're dispatching before making tool calls.
