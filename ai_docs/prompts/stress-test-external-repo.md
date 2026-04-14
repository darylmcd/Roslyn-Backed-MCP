# Stress Test: External Repo Performance Baseline

<!-- purpose: Run against a large external C# repo to capture tool-level performance baselines, identify bottlenecks, and stress recovery paths. Results are written back to the Roslyn-Backed-MCP repo for consumption by the development team. -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> **Run from the external repo's directory** (e.g., `C:\Code-Repo\jellyfin`), not from the Roslyn-Backed-MCP repo.
> All findings are written to `C:\Code-Repo\Roslyn-Backed-MCP\ai_docs\audit-reports\` using the standard naming convention.

---

## Prompt

You are a performance engineer stress-testing the Roslyn MCP server against a large external C# codebase. Your mission is to exercise every major tool category under realistic scale, capture timing baselines, identify bottlenecks, and test recovery paths.

**Output destination:** Write all results to `C:\Code-Repo\Roslyn-Backed-MCP\ai_docs\audit-reports\`. Use this naming convention:

```
{YYYYMMDD}T{HHMMSS}Z_{repo-name}_stress-test.md
```

Example: `20260413T220000Z_jellyfin_stress-test.md`

If the file gets large (>30 KB), split into parts:
- `..._stress-test-part1.md` (phases 1–4)
- `..._stress-test-part2.md` (phases 5–8)

**Persist after every phase.** Append results to the output file incrementally — do not wait until the end.

---

## Output format

Use this fixed schema for the report header:

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
| Report generated | {UTC timestamp} |
| Roslyn MCP version | {from server_info} |
```

For every tool call in every phase, record a row in this table format:

```markdown
| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 1 | compile_check | full solution | 10440 | PASS (0 errors) | baseline |
| 2 | find_references | BaseItem | 3200 | PASS (147 refs) | high-fan symbol |
```

Tag each result: **PASS**, **SLOW** (>2x budget), **TIMEOUT**, **ERROR**, **UNEXPECTED**.

Performance budgets:
- Single-symbol lookups (goto_definition, symbol_info, enclosing_symbol): ≤2s
- Search/scan (find_references, symbol_search, semantic_search): ≤10s
- Solution-wide analysis (compile_check, complexity, cohesion, dead code): ≤30s
- Mutation previews (rename, extract, code fix): ≤15s
- Build/test: ≤120s

---

## Phase 0: Setup

1. Call `server_info` — record version, capabilities.
2. Load the solution with `workspace_load` (verbose=false). Record `_meta.elapsedMs`.
3. Call `workspace_health` — record isReady, error counts, restoreHint.
4. Call `project_graph` — record project count, dependency depth.
5. If `restoreHint` is non-null, run `dotnet restore` and reload.

**Persist the report header and Phase 0 results now.**

---

## Phase 1: Compilation baseline

1. `compile_check` — full solution, severity=Error. Record error count and timing.
2. `compile_check` — full solution, severity=Warning, limit=50. Record warning count.
3. `compile_check` with `emitValidation=true` — compare timing to step 1.
4. `project_diagnostics` — limit=50, severity=Warning. Record total counts.
5. Pick the **largest project** (by DocumentCount from workspace_load). Run `compile_check` filtered to that project.

**Persist Phase 1 results.**

---

## Phase 2: Symbol resolution at scale

Pick **3 symbols** with high expected fan-out. Good candidates: a base class used across many projects, a widely-used interface, a utility method. For each:

1. `symbol_search` — find the symbol by name substring. Record hit count and timing.
2. `symbol_info` — get full metadata. Record timing.
3. `find_references` — record reference count and timing. If >100 refs, note pagination.
4. `find_references` with offset/limit pagination — fetch page 2 (offset=50, limit=50). Record timing.
5. `callers_callees` — record caller/callee counts and timing.
6. `impact_analysis` — record affected file count and timing.

Also run:
7. `symbol_search` with a **very broad query** (e.g., "Get") — record hit count and timing.
8. `semantic_search` with a complex query (e.g., "async methods returning Task") — record timing and result quality.

**Persist Phase 2 results.**

---

## Phase 3: Navigation and type hierarchy

Pick a class with a **deep inheritance hierarchy** or many implementations:

1. `type_hierarchy` — record depth, derived type count, timing.
2. `find_implementations` — for an interface, count implementations, record timing.
3. `go_to_definition` — for 5 different symbols across different projects. Record per-call timing.
4. `document_symbols` — on the **largest file** (by line count). Record symbol count and timing.
5. `get_syntax_tree` — on the same large file, maxDepth=5. Record timing and whether output was truncated.
6. `member_hierarchy` — on a virtual/override method. Record timing.

**Persist Phase 3 results.**

---

## Phase 4: Solution-wide analysis tools

Run each of these against the full solution (or largest project where solution-wide is too slow):

1. `get_complexity_metrics` — limit=20, minComplexity=10. Record timing and top hotspot.
2. `get_cohesion_metrics` — limit=20. Record timing and worst LCOM4 score.
3. `find_unused_symbols` — limit=20. Record timing and count.
4. `suggest_refactorings` — limit=10. Record timing and suggestion count.
5. `get_namespace_dependencies` — circularOnly=true. Record timing and cycle count.
6. `get_di_registrations` — record timing and registration count.
7. `get_nuget_dependencies` — record timing and package count.
8. `list_analyzers` — limit=50. Record analyzer count and timing.

**Persist Phase 4 results.**

---

## Phase 5: Mutation previews (read-only stress)

**Do NOT apply any mutations.** Preview only — we are testing a repo we don't own.

Pick targets from Phase 2/3 findings:

1. `rename_preview` — rename a symbol with 20+ references. Record timing and affected file count.
2. `rename_preview` — rename a symbol with 100+ references (if available). Record timing.
3. `extract_interface_preview` — on a concrete class with 5+ public methods. Record timing.
4. `extract_method_preview` — on a 20+ line block inside a complex method (use Phase 4 hotspot). Record timing.
5. `code_fix_preview` — pick a diagnostic from Phase 1. Record timing.
6. `fix_all_preview` — for the most common warning diagnostic, scope=project. Record timing and affected file count.
7. `format_document_preview` — on the largest file. Record timing.
8. `organize_usings_preview` — on 3 different files. Record average timing.

**Persist Phase 5 results.**

---

## Phase 6: Throughput and rapid-fire calls

Test sequential throughput — how fast can the server handle back-to-back calls:

1. Call `symbol_info` **10 times in rapid succession** on different symbols. Record per-call timing and note any degradation.
2. Call `go_to_definition` **10 times** on different symbols. Record per-call timing.
3. Call `enclosing_symbol` **10 times** on random file positions. Record per-call timing.
4. Compute: min, max, mean, p95 for each batch.

**Persist Phase 6 results.**

---

## Phase 7: Recovery and edge cases

Test error handling and recovery paths:

1. **Invalid symbol position:** Call `symbol_info` with a line/column pointing at whitespace. Record error quality.
2. **Out-of-bounds position:** Call `go_to_definition` with line=999999. Record error quality.
3. **Nonexistent file:** Call `document_symbols` with a fabricated file path. Record error quality.
4. **Stale workspace:** Call `workspace_reload`, then immediately call `compile_check`. Record timing.
5. **Double load:** Call `workspace_load` again with the same path. Verify idempotent (same workspaceId returned). Record timing.
6. **Large result set:** Call `find_references` on a symbol with the most references, limit=500. Record timing and whether pagination works.
7. **Empty search:** Call `symbol_search` with a gibberish query. Record behavior (empty results vs error).

Rate each error response: **actionable**, **vague**, or **unhelpful**.

**Persist Phase 7 results.**

---

## Phase 8: Summary and recommendations

Write a final section with:

1. **Performance summary table** — min/max/mean/p95 per tool category (lookup, search, analysis, mutation-preview).
2. **Bottleneck list** — tools that exceeded budget, sorted by severity.
3. **Scalability assessment** — "would this tool survive a 100-project solution?" rating per category.
4. **Error quality scorecard** — percentage of error responses rated actionable.
5. **Top 5 recommendations** — specific, actionable improvements ranked by impact.
6. **Comparison baseline** — if prior stress-test reports exist in the audit-reports folder, note improvements or regressions.

**Persist Phase 8 results. Report is complete.**

---

## Execution rules

1. **Never apply mutations.** Preview only. This is someone else's code.
2. **Persist after every phase.** Write incrementally to the output file.
3. **Record `_meta.elapsedMs` for every tool call.** This is the primary deliverable.
4. **If a tool times out or errors, record it and move on.** Do not retry more than once.
5. **Workspace heartbeat:** Call `workspace_health` between phases. If the workspace is gone, reload and note the incident.
6. **Context conservation:** If cumulative tool output exceeds ~200 KB, start a new turn. Summarize rather than paste raw output.
7. **Always emit text per turn:** Describe what you are dispatching before making tool calls.
