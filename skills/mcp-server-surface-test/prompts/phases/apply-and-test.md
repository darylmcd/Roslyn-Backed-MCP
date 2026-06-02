# Phase Group: Apply and Test (Phases 6 through 10)

<!-- purpose: Sub-file of the mcp-server-surface-test full prompt. Contains phases 6 (all sub-phases 6a–6z), 7, 8, 8b, 9, 10. -->
<!-- Parent orchestrator: ../full.md — read that file first for cross-cutting principles and execution strategy. -->

---

### Phase 6: Apply-tool exercise on the disposable worktree

**Apply-mode mutations happen here only**, and only inside the disposable worktree created in Phase 0 step 2. Phases 10 / 12 / 13 also drive at least one preview→apply round-trip per applicable family (against the same disposable worktree). Skip Phase 6 only when `--no-worktree` was passed; in that case state **N/A — skipped per --no-worktree flag** in the Phase 6 report section and proceed.

**The point of Phase 6 is to exercise the write path of the MCP server**, not to ship product changes. Applies are test fixtures: drive preview→apply→revert chains, verify behaviour with `compile_check` / `build_workspace` / `test_run` after each apply, and capture per-call evidence for the promotion scorecard. The disposable worktree is torn down at run end (see *Phase 6 teardown* below); nothing about Phase 6 produces a PR or a commit in the audited repo's history.

**`try/finally` discipline.** Wrap the entire Phase 6 sub-phase chain in a `try/finally` (or your host's equivalent error-handling structure) so teardown runs even if an apply fails mid-chain. The skill's correctness contract is "the disposable worktree is gone when the run ends" — apply failures are evidence to record in the report, not reasons to leave the worktree behind.

#### 6a. Fix All
1. Pick a diagnostic with many occurrences (IDE0005, CS8600).
2. `fix_all_preview(scope="solution", diagnosticId=…)`.
3. Inspect — all instances found? Probe a non-default scope (e.g. `scope="project"`).
4. `fix_all_apply(previewToken)`.
5. `compile_check`.

#### 6b. Rename
1. Pick a poorly-named symbol.
2. `rename_preview` with a better name.
3. `rename_apply`. Verify the response's `MutatedSymbol` (v1.28+) carries a fresh handle for the renamed identity — chain downstream calls off `MutatedSymbol.symbolHandle` instead of re-resolving by the new name.
4. `compile_check`; `find_references` on the new name — count matches preview.

#### 6c. Extract interface (when a consumer-heavy type was found in Phase 3)
1. `extract_interface_preview` with selected public members.
2. `extract_interface_apply`.
3. `bulk_replace_type_preview` to update consumers from concrete → interface.
4. `bulk_replace_type_apply`.
5. `compile_check`.

#### 6d. Extract type (when Phase 2 found LCOM4 > 1)
1. `find_shared_members`.
2. `extract_type_preview` with the independent cluster's members.
3. `extract_type_apply`.
4. `compile_check`.

#### 6e. Format & organize
1. `format_range_preview` / `format_range_apply` on a recently-edited region.
2. `format_document_preview` / `format_document_apply` on a heavily modified file.
3. `organize_usings_preview` / `organize_usings_apply` on files touched by code fixes.
4. `format_check` (solution-wide format verification) to confirm clean.

#### 6f. Curated code fixes
1. `code_fix_preview` on a specific diagnostic occurrence.
2. `code_fix_apply`.
3. `compile_check`.

#### 6f-ii. Diagnostic suppression
1. Pick a deliberate-pattern warning.
2. `set_diagnostic_severity` to downgrade the severity in `.editorconfig`.
3. `add_pragma_suppression` to insert `#pragma warning disable` at a specific site. After `apply`, call `verify_pragma_suppresses` on the site to confirm the pragma covers the intended diagnostic; if the scope is wrong, `pragma_scope_widen` to extend it.
4. `compile_check`.

#### 6g. Code actions
1. `get_code_actions` at a position with available refactorings.
2. `preview_code_action`.
3. `apply_code_action`.

#### 6h. Direct text edits
1. `apply_text_edit` — one small targeted edit.
2. `apply_multi_file_edit` — coordinated edits across two files.
3. `preview_multi_file_edit(fileEdits=[…])` — verify per-file diffs + one token; `preview_multi_file_edit_apply(previewToken)` to commit. **Negative probe:** mutate the workspace via a separate `format_document_apply`, then retry the now-stale token — expect a "workspace moved, regenerate preview" rejection.
4. `compile_check`.

#### 6i. Dead code removal
1. From Phase 2 `find_unused_symbols` results, pick confirmed dead symbols.
2. `remove_dead_code_preview`.
3. `remove_dead_code_apply`.
4. `compile_check`.
5. If any dead **interface** members showed up, exercise `remove_interface_member_preview(interfaceMemberHandle)`. Expect `previewToken` + `implementationCount` + implementation list, or `status=refused` with `externalCallers` populated.

#### 6j. Extract method
1. Pick a target from `suggest_refactorings` or `get_complexity_metrics` (complexity ≥ 10).
2. `analyze_data_flow` on the target range.
3. `extract_method_preview` with a descriptive name.
4. Verify the preview: parameters, return type, call site.
5. `extract_method_apply`; `compile_check`; `find_references` on the new method — ≥1 call site?

#### 6k. Advanced refactor previews (experimental; promotion-relevant)
1. **`restructure_preview`.** Pick a small idiom to normalize (e.g. `Task.Run(() => __expr__)` → `await __expr__`). Build pattern + goal as C# fragments with `__name__` placeholders. Preview on one file first, then project-wide. Apply via `preview_multi_file_edit_apply` on the returned token.
2. **`replace_string_literals_preview`.** Find a magic string in argument/initializer position (avoid XML docs / nameof / interpolation holes). Apply via `preview_multi_file_edit_apply`.
3. **`change_signature_preview`.** Pick a method with multiple callsites. Exercise `op=add`, `op=remove`, `op=rename`, and `op=reorder` in separate previews. For `op=add`, verify the default value is spliced at every callsite (named-arg callers get a named argument). For `op=reorder`, supply `newOrder` as a comma-separated permutation of parameter names or 0-based indices; verify positional callsites are reordered, all-named callsites remain semantically stable, and invalid arity / duplicate / unknown-parameter permutations return actionable errors. Apply via `preview_multi_file_edit_apply` (shared `IPreviewStore`; do **not** use `apply_composite_preview`). **Known limitation — backlog `change-signature-preview-callsite-summary` (P3):** preview `changes[].unifiedDiff` only enumerates the declaration-owner file; apply correctly rewrites every callsite. Verify post-apply with `compile_check` + `find_references`, not the preview diff. Do NOT re-raise.
4. **`symbol_refactor_preview`.** Compose rename + edit + restructure in one `operations` array (max 25 / 500 affected files). Verify each op sees rewritten state from earlier ops. Apply via `preview_multi_file_edit_apply` (shared `IPreviewStore`; **not** `apply_composite_preview`, which reads `ICompositePreviewStore` — a different store used by `extract_and_wire_interface_preview` / `class_split_preview` / `migrate_package_preview`).
5. **`change_type_namespace_preview`.** Preview moving a type's namespace while keeping its file location.
6. **`replace_invocation_preview` / `preview_record_field_addition` / `record_field_add_with_satellites_preview` / `extract_shared_expression_to_helper_preview` / `split_service_with_di_preview`.** Exercise each if the repo shape allows; mark `skipped-repo-shape` otherwise. Verify the returned preview token is accepted by the apply tool named in the tool description.

#### 6l. Atomic apply-with-verify
1. Produce a known-good preview via `organize_usings_preview`; pass the token to `apply_with_verify(previewToken, rollbackOnError=true)`. Assert `status=applied`.
2. Produce a known-bad preview (for example, `extract_method_preview` over a region that would introduce CS0136). On post-v1.15 repos the fix landed and this yields `status=applied`; on upstream regressions expect `status=rolled_back` with `introducedErrors` populated.

#### 6m. Session change tracking
1. After all Phase 6 applies, `workspace_changes` — verify every applied refactoring appears with correct descriptions, affected files, tool names, and timestamps; verify ordering.

#### 6z. Disposable worktree teardown (mandatory, runs in `finally`)

This sub-phase runs at the **end of Phase 6** as the `finally`-clause counterpart to the `try/finally` wrapping the Phase 6 chain. It also runs after Phases 10 / 12 / 13 if those phases issued additional applies inside the disposable worktree.

1. **Release Windows file locks first.** Call `workspace_close(workspaceId: <disposable-worktree-workspace-id>, drainProcesses: true)` against the workspace loaded from the disposable worktree. This is the canonical Windows teardown contract: closing the MCP session releases the host's analyzer DLL handles (`RoslynMcp.Analyzers.dll` and friends), and `drainProcesses: true` runs `dotnet build-server shutdown` atomically afterward to release `VBCSCompiler.exe` / `testhost.exe` locks on `bin/{Debug,Release}/net*/`. `dotnet build-server shutdown` alone does **not** cover the host's analyzer-DLL lock — that is what leaves the disposable worktree directory undeletable on Windows 11. If the disposable workspace was already closed in Phase 17e, skip the `workspace_close` call and run `dotnet build-server shutdown` from the audited repo root as a belt-and-braces step for any out-of-band build-server processes. (Mirrors the *Worktree teardown discipline (Windows)* contract documented in `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`.)
2. **Remove the worktree.** Run `git worktree remove --force <disposable-worktree-path>` from the audited repo root. The `--force` flag is required because Phase 6 leaves uncommitted apply-mode mutations in the worktree (intentionally — the point was to exercise the apply tools, not to commit their output).
3. **Verify cleanup.** Run `git worktree list` from the audited repo root and confirm the disposable worktree is gone. Run `git status` from the audited repo's primary checkout and confirm it is clean (Phase 6 must not have leaked changes outside the worktree).
4. **Branch cleanup.** Run `git branch -D mcp-server-surface-test/<ts>` from the audited repo root to delete the disposable branch. The branch only existed to host worktree state; it has no upstream and no history worth preserving.
5. **Record teardown outcome in the report header.** A new *Teardown* row: `clean` (worktree removed, branch deleted, primary checkout clean) / `partial — <what survived>` (e.g. `partial — branch survived; manual git branch -D required`) / `failed — <error>`.

If teardown fails for an unexpected reason, surface the failure in the report's *MCP server issues* section as a P1 finding tagged `surface-test teardown`. Do not retry blindly — the operator can clean up by hand.

**`--no-worktree` mode:** sub-phase 6z is `skipped — no worktree was created`.

**MCP audit checkpoint:** Does `fix_all_preview` find all instances without timing out? Does `rename_preview` catch references in comments/strings? Does `rename_apply.MutatedSymbol` resolve to the new identity? Does `bulk_replace_type_preview` miss any usages? Does `extract_type_preview` handle shared private members correctly? Does `format_range_preview` stay inside its range? Does `format_check` correctly report clean after the preceding format applies? Does `remove_dead_code_preview` touch only the targeted symbols? Does `set_diagnostic_severity` correctly create/update `.editorconfig`? Does `add_pragma_suppression` insert at the right line? Does `verify_pragma_suppresses` correctly validate scope? Does `extract_method_preview` infer correct parameters and return type? Do the advanced refactor previews each round-trip cleanly and return actionable errors on unsupported ops? Does `apply_with_verify` roll back cleanly on introduced errors? Does `workspace_changes` list every apply with correct ordering?

**Cross-tool chain validation.** After `rename_apply`: `find_references` on the new name = preview count. After `extract_interface_apply`: `type_hierarchy` on the new interface shows the implementor. After `fix_all_apply`: `project_diagnostics` on that diagnostic id = 0. After `organize_usings_apply`: `get_source_text` shows sorted usings. After `extract_method_apply`: `find_references` on the new method ≥ 1. After all applies: `workspace_changes` entry count matches the apply count.

**Mutation-family coverage.** By end of Phase 6 plus Phases 10 / 12 / 13, every write-capable family must be either exercised end-to-end or explicitly `skipped-safety` / `skipped-repo-shape`. A preview-only call does not cover an apply sibling unless the catalog exposes no separate apply tool.

---

### Phase 7: EditorConfig & MSBuild configuration

**Mutation isolation contract.** Step 2 writes to disk and MUST target the disposable worktree (or be marked `skipped-safety` under `--no-worktree`). It must NOT mutate the primary checkout — that would leak `.editorconfig` drift into the audited repo's working tree (see `audit-prompt-editorconfig-leak` for the failure mode this contract prevents). Step 4 reverts the worktree write so Phase 8b W2 and Phase 13 row 4/5 can re-exercise the create/update path cleanly.

1. **Read + baseline.** `get_editorconfig_options` on a source file. Do the returned options match `.editorconfig`? Capture the pre-mutation `.editorconfig` content (or the absence of the file) — needed for the revert in step 4.
2. **Write — disposable worktree only.** On the disposable worktree (default mode), call `set_editorconfig_option` to set a benign key (e.g. `dotnet_sort_system_directives_first = true`). Verify the file was created/updated. Under `--no-worktree`, mark step 2 and step 3 as `skipped-safety — --no-worktree` and proceed to 7b; do NOT call `set_editorconfig_option` against the primary checkout in any mode.
3. **Verify read-after-write.** `get_editorconfig_options` again — change reflected?
4. **Revert (mandatory).** Restore the pre-step-2 `.editorconfig` via `git -C <disposable-worktree-path> checkout -- .editorconfig` (or `git -C <disposable-worktree-path> clean -f -- .editorconfig` if step 2 created the file from scratch). Verify with `git -C <worktree-path> status --porcelain .editorconfig` returning empty. The Final surface closure's run-end git-status diff (step 3a) will catch any leak this revert misses.

#### 7b. MSBuild evaluation
1. `get_msbuild_properties` — verify key properties (`TargetFramework`, `RootNamespace`, `OutputType`).
2. `evaluate_msbuild_property(TargetFramework)` — matches step 1?
3. `evaluate_msbuild_items(itemType=Compile)` — reasonable count and paths?

**MCP audit checkpoint:** Are editor-config values accurate? Does `set_editorconfig_option` write the pair correctly? Are MSBuild property/item values consistent across the three tools?

---

### Phase 8: Build & test validation

Delegate heavy validation where possible (full-suite `test_run`, `test_coverage`, shell fallbacks). Keep selection (`test_discover`, `test_related_files`, `test_related`) in the primary agent.

1. `workspace_reload` — refresh post-Phase-6.
2. `build_workspace` — full MSBuild build.
3. `build_project` for individual projects you modified.
4. `test_discover` — find all tests.
5. `test_related_files` with the list of files you modified.
6. `test_related` on a symbol you refactored.
7. `test_run` with the filter from step 5.
8. `test_run` with no filter — full suite.
9. `test_coverage`.
10. `test_reference_map(projectName?)` — verify `{ coveredSymbols, uncoveredSymbols, coveragePercent, inspectedTestProjects, notes, mockDriftWarnings? }`. For repos using NSubstitute, check `mockDriftWarnings` flags interface methods production calls that the matching test class never stubs. For repos with no test project, the response should be a clean empty-with-reason result, not an error.
10b. `get_test_coverage_map(projectName?)` — production-symbol → covering-test-method map. Cross-check that any production symbol classified as `covered` in `test_reference_map` has at least one entry in `get_test_coverage_map`; an empty map for a `covered` symbol is a FLAG.
11. `validate_workspace(changedFilePaths=null, runTests=false)` — verify `overallStatus ∈ {clean, compile-error, analyzer-error, test-failure}`. Probe `changedFilePaths=null` to confirm auto-scoping off `IChangeTracker`. Probe `runTests=true` on the disposable worktree. **Negative probe:** fabricated `changedFilePaths` entry → clean "no related tests" result (not a crash).
12. `validate_recent_git_changes` — if git metadata is accessible, validate the last commit's touched files. Verify the bundle composes `compile_check` + diagnostics + related-tests correctly and reports a clean status on a passing commit.

If `test_discover` returns zero, record it and distinguish: `test_run` returns a clean zero-test result, `test_run` / `test_coverage` are `skipped-repo-shape`, or the server mishandles the no-test case.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` (same count / ids / locations)? Does `test_related_files` identify related tests correctly? Does `test_run` produce structured pass/fail — aggregated across projects, not just the last assembly? Does `test_coverage` produce coverage data? Does `test_reference_map.coveragePercent` agree roughly with `test_coverage`? Does `validate_workspace` produce a coherent `overallStatus`? Does `validate_recent_git_changes` succeed on a clean commit?

**Do not** call `revert_last_apply` yet — it would undo the last Phase 6 apply. Continue to Phase 8b, then Phase 10, then Phase 9.

---

### Phase 8b: Concurrency audit (per-workspace RW lock)

**Stability note.** Many AI hosts serialize MCP tool calls or cannot attribute wall-clock across truly parallel requests. That is expected. If true concurrency is unavailable, mark 8b.2 / 8b.3 / timing-sensitive parts of 8b.4 `blocked` — *client cannot issue concurrent tool calls* (or `skipped-safety` if parallel would be unsafe). Record once in the header; do not force speedup ratios against `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs`. Still run 8b.1 sequential baselines using `_meta.elapsedMs`.

**Single lock model.** One per-workspace `AsyncReaderWriterLock` via `WorkspaceExecutionGate`. `_rw-lock_` / `_legacy-mutex_` in old audit filenames are historical artifacts only.

**Purpose.** Produce a machine-readable concurrency matrix. Expected behaviour: reads overlap subject to the global throttle; writes are exclusive against in-flight readers; `workspace_close` / `workspace_reload` wait for in-flight readers.

#### 8b.0 Probe set (record in the report)

| Slot | Suggested probe (substitute equivalents) | Classification |
|---|---|---|
| R1 | `find_references` on a hot symbol used 50+ times | reader |
| R2 | `project_diagnostics` (no filters) | reader |
| R3 | `symbol_search` with >100 hits | reader |
| R4 | `find_unused_symbols(includePublic=false)` | reader |
| R5 | `get_complexity_metrics` (no filters) | reader |
| W1 | `format_document_preview` → `format_document_apply` on a Phase-6-touched file | writer |
| W2 | `set_editorconfig_option` with a benign key **on the disposable worktree** (skipped-safety under `--no-worktree`); revert via `git -C <worktree> checkout -- .editorconfig` after the write (same isolation contract as Phase 7 step 2) | writer |

#### 8b.1 Sequential baseline
Call each R1–R5 once sequentially; record `_meta.elapsedMs`. Record any structured MCP log entries (correlation ids, gate warnings, rate-limit hits, timeouts).

#### 8b.2 Parallel fan-out
`N = min(4, max(2, Environment.ProcessorCount))`. The global throttle is `max(2, Environment.ProcessorCount)` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`). Record the host core count + chosen N.
- Issue R1 ×N, R2 ×N, R3 ×N in parallel. Record wall-clock + speedup (`N × baseline / parallel`). Expected: between `0.7 × N` and `N`. < `0.7 × N` is a FLAG.
- If the client serializes calls: `blocked — client serializes tool calls`.

#### 8b.3 Read/write exclusion probe
- Start R1; while in flight, issue W1. Expected: W1 waits.
- Inverse: start W1; then issue R1. Expected: R1 waits.
- Any deviation is a FLAG.

#### 8b.4 Lifecycle stress
- Start R2; while in flight, call `workspace_reload`. Label `waits-for-reader` / `runs-concurrently` / `errors`. Record whether the reader returned stale/fresh/error.
- Start R3; call `workspace_close`. Expected: reader completes cleanly; close waits. Record the post-acquire `EnsureWorkspaceStillExists` behaviour.
- After close, `workspace_load` again for the remaining phases.

#### 8b.5 Writer reclassification

| # | Tool | Verification |
|---|---|---|
| 1 | `apply_text_edit` | Trivial edit on a Phase-6-touched file; content changes; `compile_check` passes. |
| 2 | `apply_multi_file_edit` | Coordinated edit across two files; both change; `compile_check` passes. |
| 3 | `revert_last_apply` | After row 1 or 2; prior edit reverted. *Shares evidence with Phase 9 audit-only revert.* |
| 4 | `set_editorconfig_option` | Set benign key; `get_editorconfig_options` reflects it. |
| 5 | `set_diagnostic_severity` | Set `dotnet_diagnostic.<id>.severity = suggestion`; appears in `.editorconfig`. |
| 6 | `add_pragma_suppression` | Insert `#pragma warning disable <id>` before a known diagnostic line; `get_source_text` shows the pragma. |

Writers should be measurably slower than the reader baseline — they mutate disk.

#### 8b.6 Concurrency matrix output
Use the schema in *Output Format → Concurrency matrix*. Every cell has a value or `N/A` (with a one-line reason).

**MCP audit checkpoint:** If 8b.2 was client-blocked, say so and answer **N/A — client serializes tool calls** for speedup / stability / benchmark-comparison prompts. Otherwise: did parallel reads overlap ≥ `0.7 × N`? Did the read/write probes match the documented contract? Did lifecycle stress reveal TOCTOU / stale issues? Did all six writers complete? Any gate-contention / rate-limit / deadlock entries? Is `parallel_speedup` stable across runs within ±15%?

---

### Phase 9: Undo verification (run after Phase 10)

**Why after Phase 10:** `revert_last_apply` immediately after Phase 8 would undo the last Phase 6 apply. Add one audit-only apply on top, then revert.

1. Perform exactly one low-impact Roslyn apply whose reversal is safe — `format_document_preview` → `format_document_apply` on a single file is the canonical probe (prefer one already touched in Phase 6). This becomes the new top of the undo stack.
2. `revert_last_apply` — only the audit-only apply is undone; Phase 6 changes remain.
3. `compile_check`.
4. **`revert_apply_by_sequence` — non-tip rollback.** Add a SECOND audit-only apply on top of the same Phase-6 stack (a different `format_document_apply`-style probe), then call `revert_apply_by_sequence(sequenceNumber=<the-first-audit-only-apply>)`. Verify only the targeted entry is undone; the second audit-only apply remains. Negative probe: pass an out-of-range or already-reverted sequence number — expect a clear error, not silent success. After verification, `revert_last_apply` to clear the surviving audit-only entry.
5. `compile_check`.
6. Only Roslyn solution-level changes are revertible. If `revert_last_apply` / `revert_apply_by_sequence` error or no-op, document it.

**MCP audit checkpoint:** Does `revert_last_apply` restore the prior state? Does it report what was undone? Does it return a clear "nothing to revert" when called twice in succession? Does `revert_apply_by_sequence` correctly undo a non-tip entry without disturbing later entries? Does it reject out-of-range sequence numbers cleanly?

---

### Phase 10: File, cross-project, and orchestration operations

**Default:** inspect previews for every applicable family on the disposable worktree, and drive at least one safe preview → apply → verification → cleanup chain per family that exposes an apply sibling. Under `--no-worktree`, apply siblings are `skipped-safety — --no-worktree`.

1. `move_type_to_file_preview` — move a type into its own file.
2. `move_file_preview` — move with namespace update.
3. `create_file_preview` — new source file.
4. `delete_file_preview` — unused source file (safe target).
5. Multi-project: `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `move_type_to_project_preview`.
6. If DI present: `extract_and_wire_interface_preview`.
7. If a split candidate: `split_class_preview` and/or `split_service_with_di_preview`.
8. If a package-migration candidate: `migrate_package_preview`.
9. On the disposable worktree: one safe file/type preview → apply → cleanup loop with `move_type_to_file_apply` / `create_file_apply` / `move_file_apply` / `delete_file_apply`.
10. **`apply_composite_preview` — destructive despite the name.** Only call on the disposable worktree; under `--no-worktree`, mark `skipped-safety — --no-worktree` and note the naming friction.

**MCP audit checkpoint:** Do the previews produce valid diffs with correct namespace/reference updates? Does `extract_and_wire_interface_preview` correctly identify DI registrations? Does `split_class_preview` produce valid partial classes? Does `split_service_with_di_preview` produce valid DI rewires? Does the create/move/delete/apply round-trip leave the repo clean?
