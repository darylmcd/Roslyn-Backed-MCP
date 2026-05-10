# Roslyn MCP Server — Quick Surface-Test Prompt (`--quick` tier)

<!-- purpose: Bounded read-only smoke pass for the Roslyn MCP server's live surface against a loaded C# repo. Target runtime ≤15 minutes. No apply-mode mutations, no disposable worktree, no test runs, no network-dependent calls. -->

> **This prompt is a null-op without the Roslyn MCP server.** If `mcp__roslyn__server_info` is not callable in your current tool list, stop and ask the user to start the server.

> **Primary purpose:** produce a fast read-only assessment of whether the loaded C# repo's Roslyn MCP surface is healthy enough that `--full` would yield clean evidence. No promotion scorecard. No apply round-trips. Findings still render through Phase 19 (dual-path emission — see *Finding emission* below).

The **`--full` tier** (sibling `prompts/full.md`) is the comprehensive 90–180-minute run. Use `--quick` when you want a first-look smoke pass before committing to a full audit, when you do not have the time budget for `--full`, or when the host environment cannot create a disposable worktree (`--quick` does not need one).

---

## Run shape

| Property | Value |
|---|---|
| Apply-mode mutations | **forbidden** (no `*_apply`, no `*_preview` on writers) |
| Disposable worktree | **not created** (the prompt never invokes `git worktree add`) |
| Test runs | **forbidden** (`test_run`, `test_coverage` are out of scope) |
| Network calls | **forbidden** (`nuget_vulnerability_scan` is skipped) |
| Promotion scorecard | **not emitted** (use `--full` if you need the experimental→stable signal) |
| Target runtime | **≤15 minutes** on a loaded mid-size solution |

If any phase below would require an apply or a worktree, mark it `skipped-safety — quick tier` and continue.

---

## Cross-cutting principles (apply to every call)

1. **Inline severity signal.** Tag each result **PASS** / **FLAG** / **FAIL**.
2. **Performance (`_meta.elapsedMs`).** Record per call. Quick-tier budget: single-symbol reads ≤5 s, solution scans ≤15 s. Slower than budget = FLAG, not FAIL (writer budgets are not relevant in this tier).
3. **Error message quality.** Rate every observed error **actionable** / **vague** / **unhelpful**.
4. **Cite, don't summarize.** Every finding must reference a concrete file:line and a tool call.
5. **Always emit text per turn.** Empty tool-only turns read as silent stalls.
6. **Workspace heartbeat.** Call `workspace_list` before each new phase. If the workspace is gone, reload from the recorded entrypoint.

---

### Phase -1: MCP server precondition (MUST run first, hard gate)

1. **Check the tool list.** Verify `mcp__roslyn__server_info` appears in your current tool surface. If it does not, STOP and tell the user the skill requires the Roslyn MCP server to be started.
2. **Call `server_info`.** Capture `version`, `catalogVersion`, `runtime`, `os`, `connection.state`, `surface.{tools,resources,prompts}.{stable,experimental}` counts, and `surface.registered.parityOk`. Halt if `connection.state != ready` or `parityOk == false`.
3. **Sanity-check the catalog resource.** Read `roslyn://server/catalog` and confirm per-category counts match `server_info.surface`.
4. **Workspace health probe (post-load).** After Phase 0 loads a workspace, call `workspace_health(workspaceId)` once. A non-`healthy` status before any further call is a P1 finding.

**Hard-gate checkpoint:** Is `server_info` callable? Is `connection.state == ready`? Is `parityOk == true`? Did the catalog-resource counts match `server_info`? Did `workspace_health` return `healthy`? Any `no` is a halt-or-escalate.

---

### Phase 0: Setup, live surface baseline, and repo shape

1. Pick the entrypoint: `.sln` / `.slnx` / `.csproj`. (Quick tier never creates a disposable worktree — Phase 6 is forbidden.)
2. Read `roslyn://server/resource-templates` to capture all resource URI templates.
3. Call `workspace_load` (lean summary default).
4. Call `workspace_list` to confirm the session; `workspace_status` to confirm clean load.
5. Call `workspace_warm(workspaceId)` (optional but recommended for stable timings).
6. Call `project_graph`.
7. Record repo-shape constraints (projects, tests, analyzers, source generators, DI, `.editorconfig`, CPM, multi-targeting). Do not invent applicability.
8. **Seed the coverage ledger** from the live catalog. Every tool/resource/prompt in scope for this tier ends with one final status: `exercised`, `skipped-repo-shape`, `skipped-safety — quick tier`, or `blocked`.
9. **Live-surface drift detection.** Diff the seeded coverage ledger against names referenced in this prompt's phase guidance. Names this prompt mentions but absent from the catalog → P1 FAIL under *MCP server issues* with category `prompt drift`.

**MCP audit checkpoint:** Did `workspace_load` succeed? Does `workspace_list` show the session? Did `workspace_health` return `healthy`? Are repo shape and the seeded ledger captured?

---

### Phase 1: Broad diagnostics (read-only)

1. `project_diagnostics` (no filters) for the solution-wide picture.
2. `project_diagnostics` with at least one non-default selector (project / file / severity / `offset+limit`). **Invariant check:** `TotalErrors`/`TotalWarnings`/`TotalInfo` must be invariant under `severityFilter` — only the returned arrays narrow.
3. `compile_check` (default `offset=0, limit=50`). Compare against `project_diagnostics`. Probe `severity=Error` and `file=<path>`.
4. `security_diagnostics` (OWASP-tagged).
5. `security_analyzer_status` — which analyzer packages are installed / missing.
6. `list_analyzers` — total rule count; note any LOAD_ERROR entries.
7. `diagnostic_details` on one representative error and one warning.

**Skipped in this tier:** `compile_check(emitValidation=true)` (slower variant), `nuget_vulnerability_scan` (network-dependent — mark `skipped-safety — quick tier`).

**MCP audit checkpoint:** Do diagnostic tools agree on counts? Does the non-default `project_diagnostics` path preserve invariant totals? Are `diagnostic_details` locations accurate?

---

### Phase 2: Code quality metrics (read-only)

1. `get_complexity_metrics` (no filters). Flag cyclomatic > 10 or nesting > 4.
2. `get_cohesion_metrics(minMethods=3)` to find types with LCOM4 > 1.
3. `get_coupling_metrics` if exposed; if it returns "No such tool" record the gap.
4. `find_unused_symbols(includePublic=false)`.
5. `find_dead_locals` on a few chosen methods.
6. `find_dead_fields`.
7. `get_namespace_dependencies` — circular dependencies?
8. `get_nuget_dependencies` — audit package references.
9. `suggest_refactorings` — ranked aggregation.

**Skipped in this tier:** `find_duplicated_methods`, `find_duplicate_helpers`, `find_duplicated_code` (heavier scans — defer to `--full`).

**MCP audit checkpoint:** Are complexity scores plausible? Are LCOM4 scores sane? Does `find_unused_symbols` miss obvious dead code or falsely flag used symbols? Does `suggest_refactorings` rank sensibly?

---

### Phase 3: Deep symbol analysis (one key type)

Pick ONE type that surfaces from Phase 2 (high complexity, LCOM4 > 1, or central in the project graph). Quick tier intentionally narrows from the full tier's 3–5 types.

1. `symbol_search` to locate by name.
2. `symbol_info` for metadata.
3. `document_symbols` on its file.
4. `type_hierarchy`.
5. `find_implementations` on any interface/abstract type discovered.
6. `find_references`.
7. `find_consumers` — dependency-kind classification.
8. `member_hierarchy` on overrides/implements.

**Skipped in this tier:** flow analysis (Phase 4 of `--full`), snippet/script validation (Phase 5), apply-tool exercise (Phase 6), build-and-test validation (Phase 8), concurrency audit (Phase 8b), undo verification (Phase 9), file/cross-project orchestration (Phase 10), scaffolding (Phase 12), project mutation (Phase 13). Mark each `skipped-safety — quick tier`.

**MCP audit checkpoint:** Does `find_implementations` return all concrete implementations? Are `find_references` and `find_consumers` consistent? Does `member_hierarchy` look right?

---

### Phase 11: Semantic search, discovery, and reflection/DI

1. `semantic_search("async methods returning Task<bool>")`.
2. `semantic_search("classes implementing IDisposable")` — cross-check against `find_implementations(IDisposable)`.
3. `semantic_grep` with a structural pattern. Verify the result set is non-empty on a known-good pattern, and that an intentionally bogus pattern produces a clean empty result.
4. `find_reflection_usages` — `typeof`, `GetMethod`, `Activator`, etc.
5. `get_di_registrations` — DI wiring audit.

**MCP audit checkpoint:** Do paired `semantic_search` queries return relevant differences? Does `semantic_grep` produce sensible structural matches and reject malformed patterns cleanly? Does `find_reflection_usages` find all reflection patterns? Does `get_di_registrations` parse all registration styles?

---

### Phase 14: Navigation & completions

1. `go_to_definition` on a usage → correct declaration.
2. `goto_type_definition` on a variable → the type, not the variable declaration.
3. `enclosing_symbol` inside a method → the method.
4. `get_symbol_outline` on a non-trivial file (≥3 types or ≥10 members).
5. `get_completions` after a dot.
6. `find_overrides` on a virtual method; `find_base_members` on an override.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_symbol_outline` agree with `document_symbols`? Does `get_completions` rank sensibly?

---

### Phase 15: Resource verification

1. `roslyn://server/catalog` — machine-readable surface inventory.
2. `roslyn://server/resource-templates`.
3. `roslyn://workspaces` (summary).
4. `roslyn://workspace/{id}/status` — assert `WorkspaceVersion` + `SnapshotToken`.
5. `roslyn://workspace/{id}/projects` vs `project_graph` tool output.
6. `roslyn://workspace/{id}/diagnostics` vs `project_diagnostics` tool output.
7. `roslyn://workspace/{id}/file/{filePath}/lines/{N-M}` — small known range; verify the `// roslyn://…/lines/N-M of T` marker; reject `lines/10-5` cleanly.

**MCP audit checkpoint:** Do resources agree with their tool counterparts? Are URI templates resolved correctly? Is the line-range slice marker present and the invalid range rejected cleanly?

---

### Phase 16: Prompt verification (4 prompts)

Quick tier exercises a representative subset, not the full prompt surface.

1. `explain_error` — invoke with realistic arguments from Phase 1's diagnostics.
2. `suggest_refactoring` — invoke with a concrete tool sequence from Phase 2's `suggest_refactorings`.
3. `discover_capabilities` — must align with Phase -1's live catalog.
4. `review_file` — invoke against one file from Phase 3.

For each prompt: schema sanity (argument list against `prompts/list`), rendered output correctness (every tool name in the rendered text exists in the live catalog), idempotency (same args twice → same rendered text modulo `_meta.elapsedMs`).

**MCP audit checkpoint:** Do argument templates match `prompts/list`? Does every rendered prompt reference only live-catalog tools?

---

### Final surface closure (mandatory before the report)

1. Compare the coverage ledger against the live catalog from Phase -1 / 0.
2. Every unaccounted tool/resource/prompt: assign a final explicit status with a one-line reason. The vast majority will be `skipped-safety — quick tier`.
3. *Schema vs behaviour drift*, *Error message quality*, *Performance baseline* tables populated. Every exercised tool contributes ≥1 row to *Performance baseline*.
4. *Prompt verification* has one row per exercised prompt (4).

When all phases are done, leave the workspace loaded — do not call `workspace_close` (the operator may want to follow up with `--full`).

---

## Output Format — MANDATORY

### Where to save the report

**Canonical path:** `<audited-repo-root>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`

- `<timestamp>` = current UTC `yyyyMMddTHHmmssZ`.
- `<repo-id>` = audited solution/repo name — strip `.sln` / `.slnx` / `.csproj`; lowercase; replace spaces and dots with hyphens.

The quick-tier report is structurally identical to the full-tier report but omits sections 5 (Phase 6 refactor summary), 11 (Experimental promotion scorecard), 15 (Concurrency matrix), and 16 (Writer reclassification). Those sections collapse to `**N/A — quick tier**` lines.

### Promotion scorecard JSON

Quick tier does **not** emit `_latest-promotion-scorecard.json`. Promotion-tier signal requires the apply-mode round-trips the quick tier intentionally skips. Use `--full` for promotion evidence.

### Phase 19: Finding emission

Identical to the `--full` tier's Phase 19 contract — see `prompts/full.md` *Phase 19: Finding emission (dual-path)*. Quick summary:

- **Default:** print each actionable finding's envelope to stdout as a ready-to-paste GitHub Issue body. Required envelope fields: `id`, `source-repo`, `severity`, `area`, `anchors`, `finding`, `repro`, `proposed-fix`.
- **`--auto-file`:** call `gh issue create --repo darylmcd/Roslyn-Backed-MCP --title <id> --label "area:<area>" --label "severity:<severity>" --body-file <tempfile>` per non-refused finding. Fall back to stdout-print if `gh` is missing or unauthenticated.
- **Refusal contract:** P0 or `area: security` findings are **never** auto-filed, regardless of the `--auto-file` flag. Print to stdout with the security-advisory escalation banner pointing at https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new.

`**N/A — no actionable findings**` is a valid Phase 19 outcome.

### Completion gate

The prose `.md` report must exist at the canonical path. Phase 19 must have either emitted at least one finding (stdout or `gh issue create`) OR explicitly recorded `**N/A — no actionable findings**`. The task is **incomplete** without both gates passing.
