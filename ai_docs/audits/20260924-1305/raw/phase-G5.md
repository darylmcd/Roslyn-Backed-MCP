# Phase G5 evidence — Phases 8, 8b, 9, 17 (+ workspace/server uncovered tools)

Run 20260924T130517Z. Server 4.2.1+4eba2601. W_RW=`4b02621ce1f44d3eadad0966849206b1` (later reopened, see Phase 8b.4/17e), W_RO=`bbea96a6d4504f299a1d9c48d4329994`. Paths sanitized: `<repo>` = <repo>, `<rw>` = <repo>/.worktrees/surface-test-20260924T130517Z.
Pre-state backups: `g5-editorconfig-pre.bak`, `g5-pre-{Cat,DiagnosticsProbe,G4Fixture}.cs`, `g5-md5-pre.txt` (scratch).
Operator stderr access: no. Client displays `structuredContent` when present (see F-G5-meta).

## Heartbeat / uncovered tools
| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 1 | workspace_list | — | n/a (no _meta shown) | PASS/FLAG | 2 workspaces, RW v99. No `_meta` visible (structured-content tool; see F-G5-meta). |
| 2 | server_info | — | n/a | PASS/FLAG | 4.2.1, 174 tools (113/61), parityOk, pathBoundary enforcing. No `_meta`. |
| 3 | server_heartbeat | — | n/a | PASS | state=ready, 2 workspaces, pid <pid>. No `_meta`. |
| 4 | workspace_status | RW | n/a | PASS/FLAG | v100 summary correct. No `_meta` (G8 note confirmed; root cause = structuredContent channel, WorkspaceTools.cs:383-401 UseStructuredContent=true). |
| 5 | workspace_health | RW | n/a | PASS | identical to status summary (alias). No `_meta`. |
| 6 | workspace_drift_check | RW | n/a | PASS | stale=false, filesDrifted=[], recommended=noop. No `_meta`. |
| 7 | workspace_readiness_report | RW | n/a | PASS | verdict=ready, testProjectCount=1, sourceGeneratedDocumentCount=3, limitations + next workflows sensible. |
| 8 | workspace_support_bundle | RW, maxChangeEntries=50 | n/a | PASS | status=attention-needed (3 warnings). changeLedger total 53 returned 50 (seq 4..53, hasMore). Secret scan: only workspace paths, server version, catalog version — no env vars, tokens, keys, user-profile paths. Ledger shows mixed `\` vs `/` affectedFiles (feeds F-G5-paths). |
| 9 | workspace_warm | RW projects=[SampleLib, NoSuchProject] | 4 | PASS | warmed [SampleLib]; unknown name silently skipped (as documented; no skipped[] echo — suggestion). |
| 10 | workspace_warm | RW default | 0 | PASS | 3 projects, coldCompilationCount 0. |

## Phase 8 — Build & test
| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 11 | workspace_reload | RW verbose=false | 1484 | PASS | v99→v100, gateMode=load, 0 errors. |
| 12 | build_workspace | RW | 1946 | PASS | exit 0, 0 err / 1 warn (MSTEST0032 WidgetTargetNameOnlyTests.cs:11). stdout lists the warning twice (MSBuild summary) but `diagnostics` deduped to 1 — correct. Matches compile_check (0 CS errors). |
| 13 | build_project | SampleLib | 1012 | PASS | exit 0, 0/0. |
| 14 | build_project | projectName=NoSuchProject | 0 | FLAG | `category:InvalidOperation` "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry." — **vague/misleading**: real cause (`Project 'NoSuchProject' was not found in workspace ...`, WorkspaceProjectResolver.cs:21) is discarded and reload is suggested. Returned via ValidationTools.cs:83 catch → isError=false path (G2 known, confirmed). F-G5-projnotfound. |
| 15 | test_discover | RW | 0 | PASS | 7 tests in SampleLib.Tests incl. DogGeneratedTests.Speak_Needs_Test. |
| 16 | test_related_files | Cat.cs, DiagnosticsProbe.cs, G4Fixture.cs | 53 | PASS | 5 tests via Cat.cs; missReasons explain DiagnosticsProbe/G4Fixture misses (good diagnostics). |
| 17 | test_related | metadataName=SampleLib.Cat | 0 | PASS | 1 test (GetAllAnimals_Returns_Dog_And_Cat). |
| 18 | test_related | metadataName + filePath (no line/col) | 0 | FLAG(Low) | Accepted silently; metadataName wins. Description says "three mutually-exclusive locators" / "all three required as a group" but SymbolLocatorFactory.cs:48-52 applies precedence without rejecting mixed/partial input. Coverage row `symbollocatorfactory-drift-tool-test-gap` exists (tests only). |
| 19 | test_run | filter from #16 | 1672 | PASS | 5/5 passed, structured, filter redacted in args echo. |
| 20 | test_run | no filter, compact=true | 1755 | PASS | 7/7; compact shape drops stdout/args as documented. |
| 21 | test_run | filter matching nothing, compact | 1731 | FLAG(Low) | total=0, succeeded=true, no warning that filter matched zero tests (validate_* has `test-zero-run` verdict; test_run itself is silent). |
| 22 | test_run | projectName=NoSuchTests | 0 | FLAG | same generic InvalidOperation/"call workspace_reload" message as #14 (same resolver). isError=false path (ValidationTools.cs:284/415). |
| 23 | test_coverage | RW | 6 | PASS (precondition) | success=false, errorKind=CoverletMissing, actionable install hint. Precondition, not server bug. Field `failureEnvelope.missingPackages` holds project names (`SampleLib.Tests`) not package names — naming nit. |
| 24 | test_reference_map | RW | 145 | PASS/FLAG(Low) | covered 6 / uncovered 148, coveragePercent 3.9. Denominator includes compiler-synthesized record members (`AnimalRecord.<Clone>$()`, `PrintMembers`, `Deconstruct`, `Equals(object?)`), implicit ctors (`Cat.Cat()`, `Program.Program()`), interface members (`IAnimal.Speak()`). mockDriftWarnings=[] (no NSubstitute). |
| 25 | get_test_coverage_map | RW | 1 | FLAG(Low) | Alias of test_coverage (not a production→test map as phase prompt 10b assumes) → same CoverletMissing envelope. Cross-check vs test_reference_map impossible (coverlet absent) — `skipped-repo-shape` for 10b cross-check. `deprecation.earliestRemovalMajor: 2` while server is 4.2.1 → stale lifecycle metadata (ToolAliasDeprecation.cs:49-55; same for get_symbol_outline/find_duplicated_code). F-G5-aliasmajor. |
| 26 | validate_workspace | changedFilePaths=null, runTests=false | 243 | PASS/FLAG | overallStatus=clean (in verdict table). Auto-scope from change tracker works, but `changedFilePaths` contains duplicates differing only by separator (G4Fixture.cs, G4CacheStore.cs, G4Triangle.cs each twice: `C:\...` and `C:/...`). F-G5-paths. |
| 27 | validate_workspace | null, runTests=true, summary=true | 1990 | PASS | clean; ran 5/5 tests; summary drops discoveredTests (kept filter). |
| 28 | validate_workspace | fabricated path, runTests=true, responseFormat=markdown | n/a | PASS | clean; resolved 0 / unknown 1 / discovered 0 — clean "no related tests", no crash. Markdown shape carries no `_meta` (contract note). |
| 29 | validate_recent_git_changes | summary=true | 698 | PASS | clean; 8 git-derived paths, all normalized (no dupes) — confirms the dupes in #26 come from ChangeTracker, not scoping. |

Verdict-table check: every overallStatus observed (`clean`) ∈ table (tool-usage-guide.md:103-124). PASS.
Build vs compile_check: both 0 errors. PASS.

## Phase 8b — Concurrency (host cores 24 → N=4)
Probe set: R1 find_references `RoslynMcp.Roslyn.Contracts.IWorkspaceManager` (311 refs, summary, small limit); R2 project_diagnostics (summary / limit 1); R3 symbol_search 'Service' (summary, 1000 hits); R4 find_unused_symbols(includePublic=false, limit 20); R5 get_complexity_metrics(limit 20); W1 format_document_preview→apply on Cat.cs (W_RW).

| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 30 | find_references | metadataName=RoslynMcp.Core.Services.IWorkspaceManager (wrong ns) | 7826 | FLAG | NotFound (fine) but `closestMatches` = `<>f__AnonymousType0.i/.p/.r`, `<>f__AnonymousType1.m/.n` — the real `RoslynMcp.Roslyn.Contracts.IWorkspaceManager` is absent. Root cause SymbolResolver.cs:493-495 (`query.Contains(simpleName)` gives 1-char anonymous-type property names score 3) + :254-255 ordinal tiebreak sorts `<>f__` before `R`. 7.8 s cold (closest-match scan over all compilations). F-G5-closest. |
| 31 | find_references (R1 baseline) | correct ns, limit 5 | 4 | PASS | 311 refs, paging nextOffset. |
| 32 | project_diagnostics (R2 baseline) | summary | 4 | PASS | 4405 info, 27 ids. |
| 33-35 | R3/R4/R5 baselines | symbol_search/unused/complexity | 113 / 52 / 216 | PASS | issued in one message; queuedMs 0 each. symbol_search totalCount=1000 (cap). |
| 36-47 | 8b.2 fan-out R1×4, R2×4, R3×4 | one message, 12 tool_use blocks | R1 3-4, R2 0-2, R3 21-37 | blocked | Every call queuedMs=0; all cache-hot. No timestamps exposed; no call ever showed queuing → cannot attribute overlap. **8b.2 blocked — client serializes / concurrency unattributable.** |
| 48 | project_diagnostics | Program.cs, CA1865 | 110 | PASS (known precondition) | offset1/offset2 in fan-out returned identical rows because the repo emits CA1865 twice at Program.cs:323 (duplicate NetAnalyzers ref, already recorded by G1/G4). |
| 49 | format_document_preview | Cat.cs (clean) | 5 | PASS | changes=[] — no-op preview. |
| 50 | apply_text_edit (8b.5 row1) | Cat.cs L7 whitespace, verify=true | 54 | PASS | verification clean, projectFilter SampleLib. |
| 51 | format_document_preview | Cat.cs | 4658 (queued 4639 stale auto-reload) | PASS | diff restores formatting. |
| 52 | format_document_apply + find_references(SampleLib.IAnimal) same message | W1 then R1 | 34 / 2576 (queued 2363 = staleReloadMs) | blocked | R1 queued time is the post-apply stale auto-reload, not lock wait; indistinguishable from serial dispatch. |
| 53 | apply_multi_file_edit (row2) | Cat.cs+Dog.cs L5, verify | 65 | PASS | 2 files, verification clean. |
| 54 | revert_last_apply (row3) | — | 2842 | PASS | "Apply edits to 2 file(s)" reverted; Dog.cs back to HEAD, Cat.cs L5 restored. |
| 55 | set_editorconfig_option (row4) | csharp_style_var_for_built_in_types=true:suggestion | 1 | PASS | written to <rw>/.editorconfig (L17 false→true). |
| 56 | get_editorconfig_options | Cat.cs | 6 | PASS | reflects `true:suggestion`, plus G4's CA1861=silent. |
| 57 | set_diagnostic_severity (row5) | CA1822=suggestion | 1 | PASS | appended `dotnet_diagnostic.CA1822.severity = suggestion`. |
| 58 | project_diagnostics | RW CA1822 limit 2 | 198 | PASS | 72 rows; returned L95 before L94 (unsorted within file — nit). |
| 59 | add_pragma_suppression (row6) | Dog.cs L9 CA1822 | 12 | PASS | inserts `#pragma warning disable CA1822` (no restore — documented). |
| 60 | get_source_text | Dog.cs 7-12 | 1608 (stale reload) | PASS | pragma visible. |
| 61 | revert_last_apply | — | 1558 | PASS | reverted "Apply text edit to Dog.cs" (add_pragma recorded under generic description). |
| 62 | revert_last_apply | — | 0 | PASS | "No operation to revert…" (single slot; set_* not re-revertable). |
| — | .editorconfig | cp g5-editorconfig-pre.bak | — | restored | back to G4 state (CA1861 silent line only). |
| 63 | workspace_changes | RW | 2908 (stale reload) | FLAG(Low) | 60 entries; reverted seq 56 and 59 still listed with no reverted marker (WorkspaceChangeDto has no status field). Mixed separators in affectedFiles. |
| 64 | format_document_preview | Cat.cs | 10 | PASS | |
| 65 | find_references(R1) + format_document_apply(W1) same message | R1 first | 36 / 6 | blocked | both queuedMs 0 → serialized. **8b.3 blocked — client serializes tool calls.** |

Writers (rows 1–6) all completed; writer elapsed 1–65 ms (disk writes on tiny fixture) vs reader baselines 4–216 ms — writers NOT measurably slower on this fixture (scale artefact, not a defect).

## Phase 9 — Undo
| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 66 | apply_text_edit | Cat.cs L7 deviation → seq 60 | 22 | PASS | |
| (65) | format_document_apply | Cat.cs → seq 61 | 6 | PASS | audit-only apply on top |
| 67 | revert_last_apply | — | 3176 | PASS | reverted "Format document 'Cat.cs'"; seq 60 deviation remains on disk. |
| 68 | compile_check | severity=Error | 68 | PASS | 0 errors, 3/3 projects. |
| 69 | apply_text_edit | Dog.cs L7 deviation → seq 62 | 11 | PASS | |
| 70 | revert_apply_by_sequence | 60 (non-tip) | 2908 | PASS | Cat.cs restored; Dog.cs deviation (seq 62) untouched. |
| 71 | revert_apply_by_sequence | 60 again | 1 | FLAG(Low) | reason=unknown-sequence, message "Either the sequence is from before this session, or the apply did not produce a revertable snapshot" — already-reverted not distinguished (UndoTools.cs:122, UndoService.cs:164/186). |
| 72 | revert_apply_by_sequence | 99999 | 0 | PASS | unknown-sequence, clean non-error. |
| 73 | revert_apply_by_sequence | -1 | 0 | PASS | InvalidArgument + schemaHint (actionable). isError=true. |
| 74 | revert_apply_by_sequence | 61 (reverted via revert_last_apply) | 0 | PASS/FLAG | unknown-sequence (same ambiguity as #71). |
| 75 | revert_last_apply | — | 1415 | PASS | reverted seq 62 (Dog.cs). |
| 76 | revert_last_apply | — | 0 | PASS | "No operation to revert" (double revert). |
| 77 | compile_check | Error | 57 | PASS | 0 errors. md5 of SampleLib/*.cs == pre-G5 snapshot. |
