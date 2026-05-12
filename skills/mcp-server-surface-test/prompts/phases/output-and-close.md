# Phase Group: Output and Close (Phases 11 through 19, Final Surface Closure, Output Format, Appendix)

<!-- purpose: Sub-file of the mcp-server-surface-test full prompt. Contains phases 11–19, Final surface closure, Output Format section, Promotion scorecard schema, and Appendix. -->
<!-- Parent orchestrator: ../full.md — read that file first for cross-cutting principles and execution strategy. -->
<!-- Initiative #16 (surface-test-skill-self-correction-observability) will append a self-check section after this file's content. -->

---

### Phase 11: Semantic search, discovery, and reflection/DI

1. `semantic_search("async methods returning Task<bool>")`. v1.8+ HTML-decodes ingress — a client that double-encodes (`Task&lt;bool&gt;`) gets the same results as the unencoded query.
2. `semantic_search("methods returning Task<bool>")` — broader paraphrase; explain the delta in modifier-sensitive matching.
3. `semantic_search("classes implementing IDisposable")` — cross-check against `find_implementations(IDisposable)`.
4. `semantic_grep` with a structural / regex-style pattern (e.g. a method-call shape or LINQ chain). Different surface from `semantic_search` — pattern-based rather than natural-language. Verify the result set is non-empty on a known-good pattern, and that an intentionally bogus pattern produces a clean empty result rather than a crash.
5. `find_reflection_usages` — `typeof`, `GetMethod`, `Activator`, etc.
6. `get_di_registrations` — full DI wiring audit.
7. `source_generated_documents` — source-gen outputs listed.

**MCP audit checkpoint:** Do paired `semantic_search` queries return relevant and explainable differences? Does `semantic_grep` produce sensible structural matches and reject malformed patterns cleanly? Does `find_reflection_usages` find all reflection patterns? Does `get_di_registrations` parse all registration styles? Are source-gen documents correctly listed?

---

### Phase 12: Scaffolding

**Default:** previews on the disposable worktree, plus apply one scaffold, verify, clean up. Under `--no-worktree`: apply siblings are `skipped-safety — --no-worktree`.

1. `scaffold_type_preview`. v1.8+ default is `internal sealed class`; records, interfaces, enums stay `public`. The file lands at `{projectRoot}/{namespace-folders}/T.cs` even when the namespace doesn't start with the project name. v1.17+ auto-implements interface stubs (`throw new NotImplementedException()` + required usings) when `baseType`/`interfaces` resolve to an interface with members — opt out with `implementInterface: false` and assert the body becomes empty.
2. `scaffold_test_preview` for an existing type. v1.8+ constructor-arg expressions: `IEnumerable<T>` / `ICollection<T>` / `IList<T>` / `IReadOnlyList<T>` → `System.Array.Empty<T>()`; dictionaries → `new Dictionary<K,V>()`; `string` → `string.Empty`. Previously every parameter was `default(T)`.
3. `scaffold_test_batch_preview(testProjectName, targets=[{targetTypeName, targetMethodName?}, …])` on 3–5 targets. Verify one composite token covers every generated file (not N tokens). Apply via `preview_multi_file_edit_apply` (**not** `apply_composite_preview`). Confirm every scaffolded test is discoverable.
4. `scaffold_first_test_file_preview` for a project that has no test file yet — bootstraps the test project shape.
5. Apply one scaffold via `scaffold_type_apply` (using a token from `scaffold_type_preview`) — verify the resulting file matches the preview diff and that `compile_check` passes.
6. Apply one test scaffold via `scaffold_test_apply` (using a token from `scaffold_test_preview`) — verify the new test is discoverable via `test_discover` and runs green via `test_run`.

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace and produce `internal sealed class` by default? Does the interface-stub emission toggle on `implementInterface`? Does `scaffold_type_apply` produce the file the preview promised? Does `scaffold_test_preview` produce compiling stubs that run green? Does `scaffold_test_apply` discover and run cleanly? Does `scaffold_test_batch_preview` emit one composite token? Does `scaffold_first_test_file_preview` bootstrap cleanly?

---

### Phase 13: Project mutation

**Default:** previews on the disposable worktree, plus at least one reversible preview → `apply_project_mutation` → verify → inverse preview → reverse apply. Under `--no-worktree`: apply siblings are `skipped-safety — --no-worktree`.

1. `add_package_reference_preview` / `remove_package_reference_preview`.
2. `add_project_reference_preview` / `remove_project_reference_preview`.
3. `set_project_property_preview` (e.g. `Nullable`, `LangVersion`).
4. `set_conditional_property_preview` scoped to a configuration.
5. `add_target_framework_preview` / `remove_target_framework_preview` (multi-targeting).
6. If CPM: `add_central_package_version_preview` / `remove_central_package_version_preview`.
7. `get_msbuild_properties` with `propertyNameFilter` / `includedNames` to probe a non-default path.
8. Apply + reverse one reversible mutation on the disposable worktree; verify with `workspace_reload` + `build_project` / `compile_check`.

**MCP audit checkpoint:** Do the previews produce correct XML diffs? Do conditional-property conditions evaluate correctly? Do framework additions/removals look right? Does the forward-reverse apply round-trip work? Are multi-targeting / CPM correctly marked `skipped-repo-shape` when unused?

---

### Phase 14: Navigation & completions

1. `go_to_definition` on a usage → correct declaration.
2. `goto_type_definition` on a variable → the type, not the variable declaration.
3. `enclosing_symbol` inside a method → the method.
4. `get_symbol_outline` on a non-trivial file (≥3 types or ≥10 members) — verify the outline tree depth, kind classification, and ordering match what `document_symbols` returns. Drift between these two surfaces (different kinds, different ordering, or one missing a member the other lists) is a FLAG.
5. `get_completions` after a dot. v1.8+ ranks locals/parameters → type members → types → long tail. For `filterText="To"`, in-scope `ToString` should appear before namespace-qualified externals like `ToBase64Transform`.
6. `find_references_bulk` with multiple symbol handles — batch results match individual `find_references`.
7. `find_overrides` on a virtual method; `find_base_members` on an override.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_symbol_outline` agree with `document_symbols`? Does `get_completions` rank sensibly? Does `find_references_bulk` match individual calls?

---

### Phase 15: Resource verification

1. `roslyn://server/catalog` — machine-readable surface inventory.
2. `roslyn://server/resource-templates`.
3. `roslyn://workspaces` (summary); `roslyn://workspaces/verbose` for the project tree.
4. `roslyn://workspace/{id}/status` and `/status/verbose` — assert matching `WorkspaceVersion` + `SnapshotToken`.
5. `roslyn://workspace/{id}/projects` vs `project_graph` tool output.
6. `roslyn://workspace/{id}/diagnostics` vs `project_diagnostics` tool output.
7. `roslyn://workspace/{id}/file/{filePath}` vs `get_source_text`.
8. `roslyn://workspace/{id}/file/{filePath}/lines/{N-M}` — experimental line-range slice. Request a small known range (e.g. `lines/1-10`); verify the response starts with a `// roslyn://…/lines/N-M of T` marker comment and contains only the requested lines. Request an invalid range (`lines/10-5`) — expect a structured error, not a hang.

**MCP audit checkpoint:** Do resources agree with their tool counterparts? Are URI templates resolved correctly? Do summary/verbose versions share `WorkspaceVersion` + `SnapshotToken`? Is the line-range slice marker present and is the invalid range rejected cleanly?

---

### Phase 16: Prompt verification (`prompts/list` + `prompts/get`)

The live catalog is authoritative for prompt count. Exercise every live prompt unless truly `skipped-repo-shape` or `blocked`.

**Per-prompt checklist:**

1. **Schema sanity.** Look up the prompt in `roslyn://server/catalog`; verify argument list against `prompts/list`. Mismatch = FAIL.
2. **Rendered output correctness.** Invoke with realistic arguments from Phases 1–3. Every tool name in the rendered text must exist in the live catalog. Hallucinated tool = FAIL.
3. **Actionability.** Does the rendered text produce concrete tools + preview→apply chains + verification steps?
4. **Idempotency.** Same args twice → same rendered text modulo `_meta.elapsedMs`.

**Minimum exercised set:** `explain_error`, `suggest_refactoring`, `review_file`, `discover_capabilities`. Cover the remaining live prompt surface — the current snapshot includes `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, `refactor_loop`. If the live catalog differs, trust the catalog and record prompt drift in *Improvement suggestions*.

Also exercise the prompt-renderer tool:

5. `get_prompt_text(promptName, parametersJson)` — verify the rendered messages array matches what `prompts/get` would have returned. Repeat for 2–3 prompts; no hallucinated tool names. **Negative probes:** unknown `promptName` (actionable error); malformed `parametersJson` (error cites the parse failure).

For every exercised prompt, append one row to the *Prompt verification* table.

**MCP audit checkpoint:** Do argument templates match `prompts/list`? Does every rendered prompt reference only live-catalog tools? Does `discover_capabilities` align with Phase -1's live catalog? Does `get_prompt_text` match the `prompts/get` surface?

---

### Phase 17: Boundary & negative testing

Deliberately probe edge cases. Verify inputs validated, error messages helpful, no crashes.

#### 17a. Invalid identifiers
1. Workspace-scoped tool with a non-existent workspace id → actionable error. v1.8+: error envelope's `tool` field carries the actual tool name, not `"unknown"`.
2. `find_references` / `symbol_info` with a fabricated symbol handle. v1.8+: structurally valid but unfindable handle (e.g. base64 of `{"MetadataName":"NonExistent.Type"}`) returns `category: NotFound`, not silent `{count:0, references:[]}`. Also applies to `find_consumers`, `find_type_usages`, `find_implementations`, `find_overrides`, `find_base_members`, `impact_analysis`, `find_type_mutations`.
3. `rename_preview` with the same fabricated handle.

#### 17b. Out-of-range positions
1. `go_to_definition` with line beyond end-of-file.
2. `enclosing_symbol` at line 0, column 0 (off-by-one probe).
3. `analyze_data_flow` with `startLine > endLine`.
4. `probe_position` on a position known to be whitespace.

#### 17c. Empty / degenerate inputs
1. `symbol_search("")` — empty result or clear error, not crash.
2. `analyze_snippet` with empty code body.
3. `evaluate_csharp` with empty input.

#### 17d. Stale and double-apply
1. `*_apply` with an already-consumed preview token → clear stale-token rejection.
2. Fresh preview token → advance the workspace version with a separate low-impact mutation on the disposable worktree → attempt to apply the now-stale token → clear workspace-version/staleness rejection. Under `--no-worktree`, mark `skipped-safety — --no-worktree` (no apply available to invalidate the token).
3. `revert_last_apply` twice in succession — second call returns clear "nothing to revert", not an error.

#### 17e. Post-close operations
1. `workspace_close`.
2. `workspace_status` with the now-closed id → clear error.
3. `workspace_load` to re-open (or defer reopen to end of phase if remaining phases are complete).

**MCP audit checkpoint:** For each error path — actionable, vague, or unhelpful? Did stale-token / version-mismatch cases fail cleanly? Any 500-level / unhandled exceptions? Did bad input drive the server into a degraded state affecting subsequent calls?

---

### Phase 18: Regression verification

Re-test 3–5 previously recorded issues from whichever prior source the audited repo maintains — a tracked backlog file at the repo root, a project's GitHub Issues, a prior audit report, or a saved repro list. If no prior source, `**N/A — no prior source**`.

1. Read the prior source; select 3–5 items reproducible with the current workspace.
2. For each, reproduce the exact scenario. Record **still reproduces** / **partially fixed** (describe) / **no longer reproduces — candidate for closure**.

### Phase 19: Finding emission (tri-path: fragments, auto-file, stdout-print)

For each **actionable** finding in the audit report (anything that lands in section 13 *MCP server issues* or in section 14 *Improvement suggestions* with a concrete fix sketch), render one finding envelope and emit it to **one of three destinations** depending on the `--output-mode` flag and the operator's identity.

**Routing decision (compute once, before iterating findings):**

0. **`--output-mode=fragments`** → route = **fragments**. Emit one `<audited-repo-root>/backlog.d/<finding-id>.md` per finding for `/backlog-intake` to consume. This mode is intended for maintainer-managed repos that participate in a `backlog.d/` ingestion pipeline (Roslyn-Backed-MCP and similar) and supersedes the auto-file / stdout-print branches — the operator has explicitly chosen the file-system handoff over the GitHub-Issues handoff. See *Fragments path* below for the contract.
1. **`--output-mode=findings`** (default) — fall through to the findings routing below.
2. If the skill received `--no-auto-file`, route = **stdout-print**.
3. Else if the skill received `--auto-file`, route = **auto-file** (subject to `gh` available + authenticated; otherwise fall back to stdout-print with one warning line).
4. Else probe the operator's identity by dot-sourcing the renderer and calling `Test-IsMaintainer` (which wraps `gh api user --jq .login` and compares against the upstream repo owner derived from the single `$script:UpstreamRepo` constant in `lib/render-finding.ps1`). `$true` → route = **auto-file**. `$false` (gh missing, unauthenticated, network failure, login mismatch) → route = **stdout-print**.

Record the routing decision and the maintainer-probe outcome in the audit report's *Finding emission* section before emitting.

**Envelope (shared across both destinations):**

| Field | Source |
|---|---|
| `id` | kebab-case slug; prefix with the audited repo's id (derive from `git remote get-url origin` → `owner/repo` or fall back to repo dir basename). Example: `tradewise-find-references-stale-cache`. |
| `source-repo` | audited repo's kebab-case id |
| `severity` | `P0` / `P1` / `P2` / `P3` matching section 13 severity, or `P3`/`P2` for section-14 suggestions per their workflow blocking impact |
| `area` | one of `tools` / `resources` / `prompts` / `skills` / `concurrency` / `perf` / `docs` / `security`. Pick `security` for any finding that surfaces a CVE-class issue, an authentication/authorization gap, an information leak, or any pattern that warrants pre-disclosure handling — `area: security` triggers the public-filing refusal regardless of severity. |
| `server-version` | `server_info.version` captured at Phase -1; stamp the same value on every finding in this run |
| `anchors` | one or more `path/to/file.ext:LINE` strings (relative to the audited repo's root) |
| `finding` | one to two sentences describing the bug or gap |
| `repro` | one to two sentences describing the minimal reproduction (which tool / inputs / expected vs. actual) |
| `proposed-fix` | one to two sentences pointing at the likely fix shape |

**Renderer (shared with `/backlog-intake --publish`):** rather than hand-rolling the body, dot-source the shared renderer and call its functions. The renderer is the single source of truth for the body shape — both auto-file paths emit byte-identical output, which is the contract Row 2 ships:

```
pwsh -NoProfile -Command ". '${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/lib/render-finding.ps1'; \
  $f = @{ id='<id>'; source_repo='<repo>'; severity='<sev>'; area='<area>'; \
          server_version='<ver>'; anchors=@('<a1>','<a2>'); \
          finding='<finding>'; repro='<repro>'; proposed_fix='<fix>' }; \
  Render-FindingIssue -Finding $f"
```

Returns `{title, labels, body, refusedPublic}`. Use `body` for stdout-print and `gh issue create --body-file`; respect `refusedPublic` for the P0/security refusal contract below. For fragments mode, use the sibling `Render-FindingFragment` function (same renderer file, different output template).

**Fragments path** (`--output-mode=fragments`): emit one `<audited-repo-root>/backlog.d/<finding-id>.md` fragment file per finding for `/backlog-intake` to consume. Auto-file / stdout-print routing is bypassed entirely under this mode.

Schema is canonical at `<Roslyn-Backed-MCP-root>/ai_docs/items/backlog-d-fragment-schema.md`. Required frontmatter keys: `id`, `source_audit`, `source_repo`, `severity`, `area`, `server_version`, `anchors`. Body is a single ≤6-sentence paragraph (finding + repro + proposed-fix). Use `Render-FindingFragment -Finding $f` from the shared renderer so fragment bytes match the auto-file path's GitHub-Issue body bytes (single source of truth, byte-identical contract).

Steps:

1. Ensure `<audited-repo-root>/backlog.d/` exists (`mkdir -p`).
2. For each actionable finding, derive a kebab-case `<finding-id>` that prefixes the audited repo's id (e.g. `tradewise-symbol-search-empty-query-overflow`). Filename and frontmatter `id` must match exactly.
3. `source_audit` = basename of this run's prose audit report (e.g. `20260507T203015Z_tradewise_mcp-server-surface-test.md`).
4. `source_repo` = audited repo's kebab-case id (use `Get-FindingRepoId -RepoRoot <audited-repo-root>` from the shared renderer for deterministic derivation — parses `git remote get-url origin`, falls back to the directory basename).
5. `severity` = `P0` / `P1` / `P2` / `P3` matching section 13 (or `P2` / `P3` for section-14 suggestions per their workflow blocking impact).
6. `area` = one of `tools` / `resources` / `prompts` / `skills` / `concurrency` / `perf` / `docs` / `security`. Pick `security` for any finding warranting pre-disclosure — `area: security` triggers `/backlog-intake --publish`'s public-filing refusal regardless of severity.
7. `server_version` = `server_info.version` captured at Phase -1; stamp the same value on every fragment in this run.
8. `anchors` = one or more `path/to/file.ext:LINE` strings (relative to the audited repo's root).

**Idempotency:** if a fragment with the same filename already exists in `backlog.d/`, **do not overwrite it** — leave the existing fragment in place and skip emission for that finding. Re-running the audit on the same repo without an intake-in-between is allowed; the second run is a no-op for unchanged fragments.

**N/A path:** `**N/A — no actionable findings**` is a valid Phase 19 outcome under fragments mode when sections 13 + 14 are both empty.

**Cross-repo handoff:** the prose `*_mcp-server-surface-test.md` report stays in the audited repo (per *Where to save the report* below); intake reads the fragment's `source_audit` field to back-reference the prose report when it needs additional context. No prose-report relocation happens under fragments mode.

**Stdout-print path** (non-maintainer default under `--output-mode=findings`, or `--no-auto-file` explicit): print each finding envelope to stdout as a ready-to-paste GitHub Issue body. Format (rendered by `Render-FindingIssue`):

```
## TITLE: <id>
Labels: area:<area>, severity:<severity>
Body:
- id: <id>
- source-repo: <source-repo>
- severity: <severity>
- area: <area>
- server-version: <server-version>
- anchors:
  - <anchor1>
  - <anchor2>
- finding: <finding>
- repro: <repro>
- proposed-fix: <proposed-fix>
```

**Auto-file path** (maintainer default — `gh api user --jq .login` == `darylmcd` — or `--auto-file` explicit; in both cases `gh` must be on `PATH` and `gh auth status` must be authenticated): for each non-refused finding, write the body block to a temp file and call:

```
gh issue create --repo darylmcd/Roslyn-Backed-MCP \
  --title "<id>" \
  --label "area:<area>" --label "severity:<severity>" \
  --body-file <tempfile>
```

Capture the returned Issue URL and append it to the audit report's *Finding emission* section. If `gh` is missing or unauthenticated, fall back to stdout-print and emit one warning line — do not silently drop findings.

**Refusal contract — load-bearing pre-disclosure safeguard:**

The skill **must not** call `gh issue create` for any finding whose `severity == P0` OR `area == security`. Such findings always print to stdout, regardless of detected maintainer identity, `--auto-file`, or `--no-auto-file`, prefixed with:

```
**SECURITY / P0 finding — DO NOT FILE PUBLICLY.**
Escalate via GitHub security advisories: https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new
```

The refusal is non-negotiable and applies even when the maintainer is detected or `--auto-file` is explicitly passed.

`**N/A — no actionable findings**` is a valid Phase 19 outcome when sections 13 + 14 are both empty.

---

### Final surface closure (mandatory before the report)

1. Compare the coverage ledger against the live catalog from Phase -1 / 0.
2. Every unaccounted tool/resource/prompt: call it now or assign a final explicit status with a one-line reason.
3. Confirm all audit-only mutations from Phases **7**, 8b, 9–13 were reverted or cleaned up. Only intentional Phase 6 product improvements remain on the disposable worktree (and Phase 6 itself tears the worktree down — nothing reaches the primary checkout). Explicit reverts to verify: Phase 7 step 4 `.editorconfig` checkout-revert; 8b W2 `set_editorconfig_option` checkout-revert; 8b writer-reclassification probes; W1 `format_document_apply` is the audit-only apply Phase 9 reverts.
3a. **Run-end primary-checkout clean check (HARD GATE).** Run `git -C <audited-repo-root> status --porcelain` against the audited repo's **primary checkout** (not the disposable worktree). Compare against the *Isolation baseline* captured at Phase 0 run start. Any new `M` / `A` / `D` / `??` entry that wasn't in the baseline is an **audit-prompt leak** — file as a P1 finding in *MCP server issues* with category `audit-prompt-leak`, citing the offending phase (most leaks come from a mutation phase that forgot to target the disposable worktree). The Phase 6 teardown's local check covers Phase 6; this run-end gate is the catch-all for every other phase. **Do not** auto-revert leaked files — the leak is the evidence; report it and let the operator clean up manually after reviewing.
4. Ledger totals match live catalog; catalog summary matches `server_info`.
5. *Concurrency matrix* fully populated (or the whole Phase 8b is `blocked` with a single reason).
6. *Debug log capture* has at least one entry or explicitly states `client did not surface MCP log notifications`.
7. **Self-check.** For each entry currently marked `exercised`, `exercised-apply`, or `exercised-preview-only` in the ledger, confirm the draft contains at least one tool-call result (or inline evidence line) for that tool name. Any entry lacking call evidence MUST be downgraded to `scoped-but-skipped` with a note citing the missed phase and reason. Entries marked `scoped-but-skipped` score `needs-more-evidence` in the experimental promotion scorecard — identical to `blocked`. This step runs before scorecard computation.
8. **Compute the experimental promotion scorecard.** For each experimental entry, use this rubric:
   - **`promote`** — ALL of: exercised end-to-end with ≥1 non-default parameter path; schema matched behaviour on every probe; zero FAIL findings in this run or prior backlog; p50 `elapsedMs` within budget (single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s); preview/apply round-tripped cleanly where applicable; error path actionable on ≥1 negative probe; catalog description matched actual behaviour.
   - **`keep-experimental`** — exercised with pass signal but missing ≥1 promote criterion (typically: writer round-trip not performed, or `--no-worktree` gated the apply, or a non-default path was not probed).
   - **`needs-more-evidence`** — not exercised (`skipped-repo-shape` / `skipped-safety` / `blocked` / `scoped-but-skipped`) OR one exercise too shallow to judge. Default for `blocked` and `scoped-but-skipped` entries.
   - **`deprecate`** — exercised, produced FAIL findings warranting removal. Pair with an *MCP server issues* entry.
9. *Schema vs behaviour drift*, *Error message quality*, *Parameter-path coverage*, *Performance baseline* tables populated. Every exercised tool contributes ≥1 row to *Performance baseline*; the other three can be empty-with-reason.
10. *Prompt verification* has one row per exercised prompt.

When all phases are done, `workspace_close` to release the session (if not already closed in Phase 17e).

---

## Output Format — MANDATORY

### What goes in git (nothing from this audit)

The audit run produces no commits in the audited repo's history. Phase 6 applies are exercised inside the disposable worktree the prompt creates, and the worktree is torn down at run end (sub-phase 6z). The audited repo's primary working tree and `main` branch are never mutated. The deliverable is the audit report + the promotion scorecard JSON — see below.

### What goes in a file (incremental draft + final canonical output)

Maintain a draft at `<canonical-path>.draft.md` and append findings after every phase. Never revisit prior phases by re-running tool calls — read from the draft. When all phases complete, atomically rename the draft to the canonical name.

The audit is **not finished** until both of:

1. The canonical file exists on disk at `<canonical-path>`.
2. The draft was continuously updated per phase (no one-shot final flush from working memory).

A one-shot flush from working memory is the most common cause of broken runs — the model accumulates ~1–2 MB of tool results in active context, then runs out of room when serializing the report. Persisting after each phase keeps each turn within budget and lets the next turn drop the prior phase's tool-result bulk.

This prompt writes **raw per-run evidence only**. The audit report belongs in the audited repo, not in any other location.

### Where to save the report

**Canonical path:** `<audited-repo-root>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`

- `<timestamp>` = current UTC `yyyyMMddTHHmmssZ`.
- The prose `.md` report stays in the audited repo's own `audit-reports/` directory. Cross-repo handoff to upstream happens via two channels: (a) Phase 19 finding emission (stdout-print, `gh issue create`, or `backlog.d/` fragments depending on `--output-mode`), and (b) the operator's host-side staging pipeline (e.g. `eng/stage-review-inbox.ps1` in maintainer setups), which consolidates findings into `review-inbox/` + a quorum-aware scorecard verdict. Consumers do not need to relocate the prose report manually.

### Promotion scorecard JSON (sibling artifact — MANDATORY)

In addition to the human-readable `.md` report, write a machine-readable scorecard at:

**`<audited-repo-root>/audit-reports/_latest-promotion-scorecard.json`**

The scorecard lives **next to its source evidence** in the audited repo's own `audit-reports/` folder, alongside the prose audit report. This file is **overwritten** each run. Schema:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-05-05T18:36:19Z",
  "noWorktree": false,
  "auditedRepo": "roslyn-backed-mcp",
  "auditReportPath": "audit-reports/20260505T183619Z_roslyn-backed-mcp_mcp-server-surface-test.md",
  "serverVersion": "1.33.2",
  "catalogVersion": "2026.04",
  "experimentalSurface": {
    "tools": 56,
    "resources": 4,
    "prompts": 20
  },
  "scorecard": [
    {
      "kind": "tool",
      "name": "scaffold_test_apply",
      "category": "scaffolding",
      "currentTier": "experimental",
      "recommendation": "promote",
      "evidenceCount": 7,
      "evidence": [
        "phase-12 apply round-trip clean (preview-token honored, post-apply compile_check green)",
        "phase-17 negative probe — stale token rejected with actionable error",
        "phase-8 test_discover finds new test, test_run green",
        "p50 elapsedMs=1240ms (within writer budget 30s)",
        "schema accurate vs. live behavior",
        "no entries in phase-1 debug log",
        "no relevant backlog rows"
      ],
      "blockers": []
    },
    {
      "kind": "tool",
      "name": "split_class_preview",
      "category": "refactoring",
      "currentTier": "experimental",
      "recommendation": "needs-more-evidence",
      "evidenceCount": 2,
      "evidence": [
        "phase-10 preview emitted valid partial classes",
        "p50 elapsedMs=480ms"
      ],
      "blockers": [
        "no apply round-trip exercised (apply sibling skipped-safety per --no-worktree)",
        "no negative-probe evidence on shared-state across the split"
      ]
    }
  ],
  "summary": {
    "promote": 1,
    "keep-experimental": 0,
    "needs-more-evidence": 1,
    "deprecate": 0,
    "blocked": 0
  }
}
```

**Field rules:**

- `recommendation` ∈ `"promote" | "keep-experimental" | "needs-more-evidence" | "deprecate"`. Mirror the per-call recommendation captured in the `.md` report's *Experimental promotion scorecard* section. Do not invent recommendations beyond what the human-readable scorecard contains.
- `currentTier` is the live value from `roslyn://server/catalog` per entry. Do **not** infer it from the prompt.
- `evidence` is a flat string array — one short sentence per supporting probe. Match the per-call promotion-signal lines captured in the draft (principle #14).
- `blockers` is empty when `recommendation == "promote"`; populated with concrete missing evidence otherwise.
- Skip rows for entries marked `blocked` in the coverage ledger — they cannot be scored. Track them in `summary.blocked` only.

**Why this exists.** Upstream maintainers use the per-repo scorecard plus a quorum rule (≥2 workspaces with `recommendation: "promote"`, no `keep-experimental` or `deprecate` blockers) before flipping a tool's experimental → stable tier. Without the JSON the audit's promotion lane has no operational signal. With per-repo scorecards plus quorum, single-workspace anomalies no longer drive tier decisions.

**Staleness contract.** The JSON is a snapshot, not a journal. Treat it as warn-after-30-days, ignore-after-90-days; on staleness, run `/mcp-server-surface-test` again to refresh the artifact.

**`--no-worktree` runs still emit the scorecard.** Apply round-trips that depended on the disposable worktree are recorded as `skipped-safety — --no-worktree` in the coverage ledger and the affected writer tools' `recommendation` will typically default to `needs-more-evidence`, with `blockers` citing the missing worktree. The scorecard JSON is still written so consumers see a fresh artifact and can decide for themselves whether the missing-worktree evidence matters for their gate.

### Naming scheme (`<timestamp>_<repo-id>`)

- `<timestamp>`: current UTC `yyyyMMddTHHmmssZ`.
- `<repo-id>`: audited solution/repo name — strip `.sln` / `.slnx` / `.csproj`; lowercase; replace spaces and dots with hyphens. Examples: `20260422T154500Z_itchatbot_mcp-server-surface-test.md`.

### Report contents (required sections)

The report is consumed by downstream agents. Dense tables, fixed schemas, one-line entries. Narrative paragraphs only when the issue genuinely requires one.

**Mandatory sections (always in full):**

| # | Section | Purpose |
|---|---|---|
| 1 | Header | Server, client, scale, mode, debug-log channel |
| 2 | Coverage summary | Grouped by live `Kind` + `Category` |
| 3 | Coverage ledger | One row per live tool/resource/prompt |
| 4 | Verified tools | Tested-and-working list |
| 5 | Phase 6 refactor summary | Applied product changes (or N/A + reason) |
| 6 | Performance baseline | `_meta.elapsedMs` per exercised tool |
| 7 | Schema vs behaviour drift | Principle #2 output |
| 8 | Error message quality | Principle #4 output |
| 9 | Parameter-path coverage | Principle #6 output |
| 10 | Prompt verification | Phase 16 per-prompt table |
| 11 | Experimental promotion scorecard | Per-entry recommendation |
| 12 | Debug log capture | Phase 0 channel output |
| 13 | MCP server issues (bugs) | Per-issue detail |
| 14 | Improvement suggestions | Actionable UX / output enrichment |

**Conditional sections (populate when data exists, otherwise `**N/A — <reason>**`):**

| # | Section | Populate when |
|---|---|---|
| 15 | Concurrency matrix | Phase 8b ran with at least sequential baselines |
| 16 | Writer reclassification verification | Phase 8b.5 exercised writers |
| 17 | Response contract consistency | Principle #5 observed ≥1 inconsistency |
| 18 | Known issue regression check | Prior source existed |
| 19 | Known issue cross-check | New findings matched a backlog/issue id |

Mandatory sections always render in full; conditional sections collapse to a single `**N/A — <reason>**` line when unpopulated.

### Markdown template

```markdown
# MCP Server Audit Report

## 1. Header
- **Date:**
- **Audited solution:**
- **Audited revision:** (commit / branch if available)
- **Entrypoint loaded:**
- **Flags:** (none) / `--no-worktree`
- **Isolation:** (absolute disposable worktree path + branch name, or `degraded — --no-worktree flag, Phase 6 applies skipped`)
- **Teardown:** `clean` / `partial — <what survived>` / `failed — <error>` / `N/A — --no-worktree`
- **Client:** (name/version; note if prompts or resources are client-blocked)
- **Workspace id:**
- **Warm-up:** `yes` / `no` (did you call `workspace_warm` after load?)
- **Server:** (from `server_info`)
- **Catalog version:** (from `server_info.catalogVersion`)
- **Roslyn / .NET:** (if reported)
- **Live surface:** `tools: <stable>/<experimental>`, `resources: <stable>/<experimental>`, `prompts: <stable>/<experimental>` (from `server_info.surface`)
- **Scale:** ~N projects, ~M documents
- **Repo shape:**
- **Prior issue source:**
- **Debug log channel:** `yes` / `partial` / `no`
- **Report path note:** (path under the audited repo's `audit-reports/`; cross-repo handoff is via Phase 19 fragments, not via copying the prose report)

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Scoped-but-skipped | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------------------|-------|

## 3. Coverage ledger
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|

## 4. Verified tools (working)
- `tool_name` — one-line observation (include p50 elapsedMs when available)

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** absolute path (or `N/A — --no-worktree`)
- **Disposable branch:** `mcp-server-surface-test/<ts>` (or `N/A — --no-worktree`)
- **Scope:** (which 6a–6m sub-phases ran; `**N/A — skipped per --no-worktree**` when degraded mode)
- **Apply-tool calls:** bullets — preview→apply pairs exercised, with tool name + outcome
- **Verification:** `compile_check` / `test_run` / `build_workspace` outcomes after applies
- **Teardown outcome:** see header *Teardown* row; expand here if `partial` / `failed`

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|

## 11. Experimental promotion scorecard
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|

## 12. Debug log capture
| timestamp | level | logger | correlationId | eventName | message | Phase | Tool in flight |
|-----------|-------|--------|----------------|-----------|---------|-------|----------------|

## 13. MCP server issues (bugs)

### 13.1 <title or tool name>
| Field | Detail |
|-------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |

(repeat per issue, or write `**No new issues found**` when none)

## 14. Improvement suggestions
- `tool_name` — suggestion (UX / missing feature / workflow gap / output enrichment / schema mismatch)

## 15. Concurrency matrix (Phase 8b)

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|

### Sequential baseline (single-call wall-clock, ms)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|

### Parallel fan-out and behavioral verification
- **Host logical cores:** _
- **Chosen N:** _N = min(4, max(2, logical_cores))_

| Slot | Parallel wall-clock (ms) | Speedup vs baseline | Expected | Pass / FLAG / FAIL | Notes |
|------|---------------------------|----------------------|----------|---------------------|-------|

### Read/write exclusion behavioral probe
| Probe | Observed | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|----------|---------------------|-------|

### Lifecycle stress
| Probe | Observed | Reader saw | Reader exception | correlationId | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|-----------|------------------|---------------|----------|---------------------|-------|

## 16. Writer reclassification verification (Phase 8b.5)
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|

## 17. Response contract consistency
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|

## 18. Known issue regression check (Phase 18)
| Source id | Summary | Status |
|-----------|---------|--------|

## 19. Known issue cross-check
- bullet list of newly observed issues that match a prior source
```

### Completion gate

The prose `.md` report must exist at the canonical path above (under the audited repo's `audit-reports/`). Create the directory if missing. Phase 19 must have emitted at least one fragment under `<audited-repo-root>/backlog.d/` OR explicitly recorded `**N/A — no actionable findings**`. The task is **incomplete** without both gates passing.

---

## Appendix — intentionally minimal

The body of this prompt **does not** embed hard-coded tool, resource, prompt, or skill counts, nor per-version change logs. Those always drift. Instead, the live surface comes from three sources the running server emits:

| Source | What it gives you | When captured |
|---|---|---|
| `server_info` | Version, catalog version, tier counts, `surface.registered.parityOk`, connection state | Phase -1 |
| `roslyn://server/catalog` | Per-entry `Kind`, `Category`, `SupportTier`, metadata | Phase -1 / 0 |
| `roslyn://server/resource-templates` | All resource URI templates | Phase 0 |

That is the authoritative surface for any run. Trust those captures over any prose in this prompt.

### Historical notes (kept terse, rotate when obsolete)

- **Script timeout.** `evaluate_csharp` honors `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` (default 10 s). Principle #9 governs multi-minute stalls.
- **Write-lock model.** One per-workspace `AsyncReaderWriterLock` via `WorkspaceExecutionGate`. No dual-lock lane (historical `_rw-lock_` / `_legacy-mutex_` audit filenames are artifacts).
- **v1.28+.** `workspace_warm` is the recommended post-`workspace_load` prime step. `rename_apply.MutatedSymbol` carries a fresh handle for chained calls.
- **Experimental-promotion workflow.** The promotion scorecard (mandatory output of every run) replaces the deprecated standalone experimental-promotion prompt. Scorecard-only runs are no longer a separate mode — the canonical run always exercises the experimental surface and emits the scorecard.
