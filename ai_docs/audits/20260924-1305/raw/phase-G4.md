# Phase G4 evidence — Phase 6 (6a–6m) + Phase 7 — W_RW 4b02621ce1f44d3eadad0966849206b1

RW root: `<repo>/.worktrees/surface-test-20260924T130517Z`. Initial `git status --porcelain`: **empty (clean)**.

## Call log
| # | Sub | Tool | Key inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|---|
| 1 | hb | workspace_list | — | n/a | PASS | 2 workspaces; W_RW v1 ready, 37 docs |
| 2 | 6a | project_diagnostics | summary=true, severity=Hidden | 256 | FLAG | CA1822=64, but only 32 distinct sites — every analyzer diag is emitted twice (see F-diag-dup) |
| 3 | 6a | fix_all_preview | CA1822, solution | 122 | FAIL | "No code fix provider is loaded for 'CA1822'" — yet code_fix_preview finds MarkMembersAsStaticCodeFix (#8). Error msg: misleading (tells user to restore packages) |
| 4 | 6a | project_diagnostics | severity=Hidden, limit=80 | n/a (64 KB spilled) | FLAG | raw analyzerDiagnostics has 74 entries; 64 CA1822 of which 32 unique (exact duplicates) |
| 5 | 6a | evaluate_msbuild_items | SampleLib, Analyzer | 71 | PASS | 2 SDK analyzer DLLs (static eval; package analyzers from ResolvePackageAssets not shown — expected) |
| 6 | 6a | list_analyzers | SampleLib, limit=3 | 1 | PASS | analyzerCount=10 but only 6 distinct assemblyName groups → duplicate analyzer refs (SDK NetAnalyzers + package NetAnalyzers via root Directory.Build.props) |
| 7 | 6a | list_analyzers | SampleLib, limit=400 | n/a (117 KB spilled) | PASS | groups: BannedApi, CSharp.NetAnalyzers(37), NetAnalyzers(280), 3 generators |
| 8 | 6f | code_fix_preview | CA1822 Cat.cs 9:17 | 465 | PASS | MarkMembersAsStaticCodeFix, 1 file |
| 9 | 6f | code_fix_apply | token f299… | 52 | PASS | appliedFiles=[Cat.cs]; git diff --stat = Cat.cs 1+/1- — matches preview |
| 10 | 6f | compile_check | severity=Error | 1729 (auto-reload 1635) | PASS | 0 errors |
| 11 | 6a | fix_all_preview | MSTEST0046, scope=project SampleLib.Tests | 5 | FAIL | "No code fix provider" — code_fix_preview (#12) finds the MSTest fixer |
| 12 | 6a | code_fix_preview | MSTEST0046 AnimalFormatterTests 22:9 | 38 | PASS | fixer found; token discarded (not applied) |
| 13 | 6a | create_file_preview | SampleLib/G4Fixture.cs (fixture: interface+2 impls, record, LCOM>1 class, consumer, dead members, unused usings) | 16 | PASS | 1 file diff |
| 14 | 6a | create_file_apply | | 470 | PASS | git: `?? G4Fixture.cs` only |
| 15 | 6a | compile_check | severity=Warning | 1619 | PASS | 0 err; CS0414 x2 (DiagnosticsProbe, G4Fixture._neverRead) |
| 16 | 6a | fix_all_preview | CS8019, solution | 6 | PASS (known) | no provider — pinned by FixAllServiceIntegrationTests.cs:50 |
| 17 | 6a | fix_all_preview | IDE0005, document | 1 | FLAG | no provider; guidance points to organize_usings (actionable) |
| 18 | 6a | code_fix_preview | CS8019 G4Fixture 1:1 | 9 | FLAG | curated remove_unused_using: diff replaces `using System.Text;` with an empty line instead of deleting the line (Low) |
| 19 | 6a | fix_all_preview | IDE0290, document G4Fixture | 114 | PASS | 1 fix, primary ctor |
| 20 | 6a | fix_all_preview | IDE0290, solution | 69 | PASS | fixedCount=6 across 6 files (ConventionFixtures, G4Fixture, Circle, EquatablePoint, Rectangle, MultiNamespaceService) — non-default scope probe |
| 21 | 6a | fix_all_apply | doc-scope token d51c… (valid: no apply since mint) | 5 | PASS | appliedFiles=[G4Fixture.cs]; git status unchanged set (G4Fixture untracked + Cat.cs) — matches |
| 22 | 6a | compile_check | Error | 2770 | PASS | 0 errors |
| 23 | 6a | project_diagnostics | diagnosticId=IDE0290, summary | 556 | FLAG | totalDiagnostics=0 though schema says total* ignore diagnosticId filter (unfiltered total was 75). IDE* rules never appear in project_diagnostics → chain check vacuous |
| 24 | 6b | rename_preview | G4Fixture.Log @38:18 → AppendLog | 115 | PASS | 1 file, decl + 2 refs |
| 25 | 6b | rename_apply | | 22 | PASS | appliedFiles=[G4Fixture.cs]; mutatedSymbol populated with fresh symbolHandle (AppendLog(string) @38:18) |
| 26 | 6b | find_references | symbolHandle=mutatedSymbol.symbolHandle, summary | 1474 (auto-reload 1437) | PASS | 2 refs = preview count; handle chain works |
| 27 | 6b | compile_check | Error | 3663 (auto-reload 3599) | FLAG | 0 errors, but a SECOND auto-reload with no intervening mutation (find_references already reloaded) — observation |
| 28 | 6c | extract_interface_preview | G4Fixture → IG4Fixture, 4 members | 18 | FLAG | correct, but base list emitted on its own line `public class G4Fixture\n : IG4Fixture` (InterfaceExtractionService.cs:133 — identifier trailing newline precedes base list); new file has redundant `using System;` |
| 29 | 6c | extract_interface_apply | | 536 | PASS | 2 files (IG4Fixture.cs new, G4Fixture.cs) = preview |
| 30 | 6c | bulk_replace_type_preview | SampleLib.G4Fixture → SampleLib.IG4Fixture, scope default | 1695 (auto-reload) | FLAG | 3 refs replaced correctly (primary ctor param, field, method param) but injects redundant `using SampleLib;` into a file already in `namespace SampleLib;` (BulkRefactoringService.cs:114-125 EnsureUsingDirective only checks existing usings, not enclosing namespace) |
| 31 | 6c | bulk_replace_type_apply | | 3 | PASS | 1 file = preview |
| 32 | 6c | compile_check | Error | 1553 | PASS | 0 errors |
| 33 | 6d | find_shared_members | metadataName SampleLib.G4Fixture | 5 | PASS | 3 shared: _counter, _cache, AppendLog — correct |
| 34 | 6d | extract_type_preview | members Lookup,Store,CacheHits,_cache,_cacheHits → G4Cache | 45 | FAIL | no warning that Lookup implements IG4Fixture.Lookup → CS0535 (proved by #35); moved private fields made `public`; whole compilation unit NormalizeWhitespace'd (reformats unrelated members + G4Consumer, removes blank lines) — TypeExtractionService.cs:127, :568/:1024 |
| 35 | 6l | apply_with_verify | token from #34, rollbackOnError=true | 2480 | PASS | status=rolled_back, introducedErrors=[CS0535 IG4Fixture.Lookup], pre 0/post 1; disk verified: G4Cache.cs removed, G4Fixture.cs restored |
| 36 | 6d | extract_type_preview | Store,CacheHits,_cache,_cacheHits → G4Cache, newFilePath=G4CacheStore.cs (non-default) | 57 | FLAG | Lookup rewired to `_g4Cache._cache` (shared private handled by exposing it public); same public-field + whole-file reformat defects; Store/CacheHits dropped from G4Fixture API with no forwarding; ctor now requires G4Cache |
| 37 | 6d | extract_type_apply | | 446 | PASS | 2 files (G4CacheStore.cs new, G4Fixture.cs) = preview |
| 38 | 6d | compile_check | Error | 1444 | PASS | 0 errors |
| 39 | 6e | format_check | projectName=SampleLib | 30 | PASS | 25 docs, 3 violations (AnimalService pre-existing, G4Fixture, IG4Fixture) |
| 40 | 6e | format_range_preview | G4Fixture 1:1-30:1 | 7 | FAIL | `InvalidOperation: "The operation is not valid in the current state. Check the tool contract and retry."` — unhelpful. Real cause: SpliceFormattedRange refuses because whole-doc format adds 1 line (RefactoringService.cs:1185-1203); its specific message is redacted by ToolErrorHandler.cs:152-170 |
| 41 | 6e | format_document_preview | G4Fixture | 6 | PASS | 1 change: blank line after `namespace` (the line-count change) |
| 42 | 6e | format_range_preview | G4Fixture 60:1-80:1 (50+ lines from change) | 5 | FAIL | same generic InvalidOperation — a range far from the only formatting change is refused; tool unusable whenever any blank-line normalization exists anywhere in the file |
| 43 | 6e | format_document_apply | token from #41 | 9 | PASS | 1 file = preview; blank line added |
| 44 | 6e | organize_usings_preview | G4Fixture | 1501 | PASS | removes 4 unused + redundant `using SampleLib;` |
| 45 | 6e | organize_usings_apply | | 4 | PASS | 1 file = preview |
| 46 | 6l | organize_usings_preview | G4CacheStore.cs | 1737 | PASS | same 5 usings |
| 47 | 6l | apply_with_verify | token #46, rollbackOnError=true | 30 | PASS | status=applied, pre 0/post 0; disk verified |
| 48 | 6h | apply_text_edit | G4Fixture insert @91:1 badly-spaced `Twice`, verify=true (non-default) | 1412 | PASS | diff exact; verification.status=clean projectFilter=SampleLib |
| 49 | 6e | format_range_preview | G4Fixture 91:1-91:40 | 1332 | PASS | only line 91 reformatted |
| 50 | 6e | format_range_apply | | 4 | PASS | disk line 91 = `public int Twice(int x) => x * 2;`; stayed in range |
| 51 | 6e | format_check | (all projects) | 1325 | PASS | 30 docs, 2 violations: AnimalService (pre-existing), IG4Fixture (extract_interface output not formatter-clean) — G4Fixture now clean |
| 52 | 6h | apply_multi_file_edit | IG4Fixture +blank line, G4CacheStore +comment; verify=true, autoRevertOnError=true | 97 | PASS | filesModified=2, per-file diffs, verification clean; disk matches |
| 53 | 6h | preview_multi_file_edit | G4Fixture @21 + IG4Fixture @5 comment markers | 1516 | PASS | 2 per-file diffs, one token |
| 54 | 6h | preview_multi_file_edit_apply | | 5 | PASS | 2 files = preview; grep confirms both markers |
| 55 | 6h | apply_text_edit | G4CacheStore L13 bad spacing (setup for stale probe) | 1306 | PASS | CRLF preserved |
| 56 | 6h | preview_multi_file_edit | IG4Fixture "STALE TOKEN SHOULD NOT LAND" (token B) | 1304 | PASS | |
| 57 | 6h | format_document_preview | G4CacheStore | 6 | PASS | L13 fix |
| 58 | 6h | format_document_apply | | 3 | PASS | 1 file |
| 59 | 6h | preview_multi_file_edit_apply | stale token B | 1293 | PASS | success=false, "Preview token is invalid, expired, or stale because the workspace changed…Please create a new preview." — actionable; grep confirms marker absent |
| 60 | 6h | compile_check | Error | 70 | PASS | 0 errors |
| 61 | 6f-ii | set_diagnostic_severity | CA1861 silent, filePath=AnimalServiceTests.cs | 4 | FLAG | wrote root `.editorconfig` but appended a NEW `[*.{cs,csx,cake}]` section instead of using the existing `[*.cs]` section (EditorConfigService.cs:370 canonical section) |
| 62 | 6f-ii | project_diagnostics | CA1861, Hidden | 89 | FAIL | CA1861 still reported at severity Info after the downgrade to silent; no auto-reload (workspace isStale=false) — editorconfig writes never invalidate the workspace (EditorConfigService.cs:330-397); prior fix `editorconfig-write-no-auto-invalidation` patched only the reader |
| 63 | 6f-ii | workspace_list | | n/a | PASS | W_RW v37, isStale=false (confirms no invalidation) |
| 64 | 6f-ii | add_pragma_suppression | DiagnosticsProbe L9 CS0414 | 99 | FLAG | inserted at the right line, but as an unpaired `#pragma warning disable` (no restore → suppresses to EOF) |
| 65 | 6f-ii | verify_pragma_suppresses | L10 | 1826 | PASS | suppresses=true, reason names dangling disable |
| 66 | 6f-ii | apply_text_edit | insert `#pragma warning restore CS0414` @10 | 25 | PASS | setup: field uncovered |
| 67 | 6f-ii | verify_pragma_suppresses | L11 (negative) | 4416 | PASS | suppresses=false, diagnosticFiresAtLine=true, precise reason |
| 68 | 6f-ii | pragma_scope_widen | L11 | 34 | FLAG | success, but restore moved to line 12 AFTER the class `}` rather than directly after the field (over-widen by 1 line; harmless) |
| 69 | 6f-ii | verify_pragma_suppresses | L10 | 4143 | PASS | covered (9→12) |
| 70 | 6f-ii | compile_check | Warning | 2479 | PASS | DiagnosticsProbe CS0414 gone; only G4Fixture._neverRead CS0414 left |
| 71 | 6g | get_code_actions | G4Fixture 72:21-72:34 (selection, non-default range path) | 296 | PASS | 6 Introduce-parameter actions |
| 72 | 6g | preview_code_action | actionIndex=2 (into new overload) | 140 | PASS | new 3-param overload + forwarding 2-param body |
| 73 | 6g | apply_code_action | | 18 | PASS | 1 file = preview |
| 74 | 6g | compile_check | Error | 2063 | PASS | 0 errors |
| 75 | 6i | find_unused_symbols | SampleLib, limit=30 | 12 | PASS | 8 results incl. NeverCalled (high); _neverRead correctly absent (written in NeverCalled) |
| 76 | 6i | remove_dead_code_preview | [NeverCalled handle] | 9 | FLAG | only target removed, but leaves a whitespace-only `    ` line → 2 blank lines + trailing ws (DeadCodeService.cs:129 `SyntaxRemoveOptions.KeepExteriorTrivia`) |
| 77 | 6i | remove_dead_code_apply | | 6 | PASS | 1 file = preview; disk shows `    $` residue line |
| 78 | 6i | compile_check | Warning | 1682 | PASS | 0 err; CS0169 _neverRead now never used (expected) |
| 79 | 6i | symbol_search | UnusedShapeName, summary | 111 | PASS | 3 hits (iface + 2 impls) |
| 80 | 6i | remove_interface_member_preview | IG4Shape.UnusedShapeName handle | 42 | PASS | status=previewed, implementationCount=2, externalCallers=[], note names remove_dead_code_apply; same whitespace residue ×3 |
| 81 | 6i | remove_dead_code_apply | token #80 (cross-route) | 5 | PASS | 1 file; grep UnusedShapeName=0 |
| 82 | 6i | compile_check | Error | 1995 | PASS | 0 errors |
| 83 | 6j | analyze_data_flow | G4Fixture 70-75 | 18 | PASS | in: name,count,total; out: line — correct (locals shown as `string?` — var nullable annotation) |
| 84 | 6j | extract_method_preview | 70:9-75:10 → BuildLine | 19 | FAIL | params correct but return type inferred `string?` (from var local's annotated type) → introduces CS8603 at call site (#86). ExtractMethodService.cs:257 uses `flowsOut[0].Type` display string (declared nullable annotation of `var`) instead of flow state |
| 85 | 6j | extract_method_apply | | 6 | PASS | 1 file = preview |
| 86 | 6j | compile_check | Warning | 1927 | FAIL | NEW CS8603 "Possible null reference return" at G4Fixture:71 introduced by extract_method (would fail builds with TreatWarningsAsErrors, e.g. this repo's Directory.Build.props) |
| 87 | 6j | find_references | BuildLine @74:21 | 9 | PASS | 1 call site |
| 88 | 6k.1 | restructure_preview | `__a__ * 2`→`__a__ + __a__`, filePath=G4Fixture | 8 | PASS | 2 matches |
| 89 | 6k.1 | restructure_preview | same, projectName=SampleLib (non-default scope) | 7 | PASS | 5 matches / 2 files (incl. interpolation hole in RefactoringProbe) |
| 90 | 6k.1 | preview_multi_file_edit_apply | file-scoped token #88 | 5 | PASS | 1 file = preview |
| 91 | 6k.1 | restructure_preview | `__a__ + 1`→`__a__ * 3` on `count + count + 1` (precedence probe) | 1814 | FAIL | output `count + count * 3` — silent semantic change, compiles; captured node spliced without parenthesization (RestructureService.cs:403-414 PlaceholderSubstituter.VisitIdentifierName). Not applied |
| 92 | 6k.4 | symbol_refactor_preview | [rename Twice→Double, edit insert const, restructure `__a__ + __a__`→`2 * __a__`] | 85 | FLAG | ops chain correctly (restructure saw renamed state), but changes[] has 2 entries for the SAME file with different path spellings (backslash vs caller's forward-slash) and the rename delta appears in neither diff |
| 93 | 6k.4 | preview_multi_file_edit_apply | token #92 (route named in skill prompt) | 0 | FLAG | `PreviewTokenStale: "expired because the workspace was reloaded"` — misleading: token lives in ICompositePreviewStore (SymbolRefactorService.cs:67,121); any unknown token gets the "reloaded" message (ToolDispatch.cs:394-400). Tool description names no apply route |
| 94 | 6k.4 | compile_check | Error | 35 | PASS | nothing applied |
| 95 | 6k.4 | symbol_refactor_preview | same (re-mint) | 16 | FLAG | same as #92 |
| 96 | 6k.4 | preview_multi_file_edit_apply | token #95 immediately | 0 | FLAG | reproducible stale rejection |
| 97 | 6k.4 | symbol_refactor_preview | same (re-mint) | 16 | FLAG | same |
| 98 | 6k.4 | apply_composite_preview | token #97 | 1795 | PASS | all 3 ops landed (const L25, Describe `2 * count + 1`, `Double(int x) => 2 * x`), 1 file |
| 99 | 6k.4 | compile_check | Error | 67 | PASS | 0 errors |
| 100 | 6k.2 | replace_string_literals_preview | "Report:"→G4Fixture.ReportPrefix, filePath | 5 | FAIL | rewrites the const's own initializer → `const string ReportPrefix = G4Fixture.ReportPrefix;` (CS0110 circular), no warning (StringLiteralReplaceService.cs:162 accepts every EqualsValueClause) |
| 101 | 6k.2 | apply_with_verify | token #100 (cross-family) | 1768 | PASS | status=rolled_back, introducedErrors=[CS0110]; disk restored |
| 102 | 6k.2 | replace_string_literals_preview | "dec"→`"d" + "ec"`, projectName=SampleLib | 15 | PASS | 1 site |
| 103 | 6k.2 | preview_multi_file_edit_apply | | 6 | PASS | 1 file = preview |
| 104 | 6k.3 | create_file_preview | SampleLib/G4Triangle.cs (2nd-file IG4Shape impl + named-arg caller + Summarize(title,count,verbose) with positional/named/mixed callers) | 1911 | PASS | |
| 105 | 6k.3 | create_file_apply | | 564 | PASS | |
| 106 | 6k.3 | compile_check | Error | 1827 | PASS | 0 |
| 107 | 6k.3 | change_signature_preview | op=add IG4Shape.Area precision:int=2 | 67 | FAIL | changes[] = G4Fixture.cs (interface only) + G4Triangle.cs (impl + named call). G4Square.Area/G4Circle.Area (same file as interface) NOT rewritten; callsiteUpdates=1 (5 call sites exist); named splice `precision:2` (no space). Root cause: ChangeSignatureAddRemovePreviewBuilder.cs:71-77 resolves `mds.Span` from ORIGINAL tree against the accumulated (already-edited) document → later same-doc decls shift and FirstAncestorOrSelf returns null → silently skipped (same pattern for callers :146-153) |
| 108 | 6k.3 | preview_multi_file_edit_apply | token #107 | 7 | PASS* | disk md5 diff = exactly G4Fixture.cs + G4Triangle.cs (= preview) — preview/apply parity holds |
| 109 | 6k.3 | compile_check | Error | 1669 | FAIL | CS0535 ×2 (G4Square, G4Circle don't implement Area(int,int)) — introduced by #107 |
| 110 | 6k.3 | revert_last_apply | | 1874 | PASS | reverted=true; md5 byte-identical to pre-apply snapshot |
| 111 | 6k.3 | change_signature_preview | op=remove Summarize `verbose` | 70 | FLAG | all 3 callsites (positional/named/mixed) fixed, but body still references `verbose` → CS0103 with no warning. Preview only |
| 112 | 6k.3 | change_signature_preview | op=rename title→heading | 21 | PASS | decl, body refs, named arg |
| 113 | 6k.3 | change_signature_preview | op=reorder `count,title,verbose` | 5 | FAIL(err) | generic "The operation is not valid in the current state" — real reason (mixed positional+named callsite refusal, ChangeSignatureService.cs ~L332) redacted by ToolErrorHandler.cs:152-170. Unhelpful |
| 114 | 6k.3 | change_signature_preview | op=reorder `count,count,verbose` (dup) | 0 | FLAG | InvalidArgument "Parameter 'newOrder' is invalid…" + schemaHint — vague (duplicate not named); BuildSafeArgumentMessage ToolErrorHandler.cs:569 drops raw message |
| 115 | 6k.3 | change_signature_preview | op=reorder `count,bogus,verbose` (unknown) | 0 | FLAG | same vague message |
| 116 | 6k.3 | change_signature_preview | op=reorder IG4Fixture.Describe newOrder=`1,0` (indices) | 11 | PASS | iface (IG4Fixture.cs) + impl + positional caller reordered; all-named caller untouched |
| 117 | 6k.3 | preview_multi_file_edit_apply | | 7 | PASS | disk md5 diff = exactly G4Fixture.cs + IG4Fixture.cs |
| 118 | 6k.3 | compile_check | Error | 1745 | PASS | 0 |
| 119 | 6k.5 | change_type_namespace_preview | G4Triangle SampleLib→SampleLib.Shapes (no newFilePath) | 60 | FAIL | emits `namespaceSampleLib.Shapes{` (missing space) AND a block namespace beside a file-scoped one (illegal, CS8955) — NamespaceRelocationService.cs:371-376 |
| 120 | 6k.5 | apply_with_verify | token #119 | 1620 | PASS | rolled_back, 8 introducedErrors (CS1519/CS1001/CS1513/CS1022/CS0246 'namespaceSampleLib'…); disk restored |
| 121 | 6k.6 | apply_text_edit | add SummarizeV2(bool verbose,string title,int count) => Summarize(...), verify=true | 72 | PASS | clean |
| 122 | 6k.6 | replace_invocation_preview | Summarize(string,int,bool)→SummarizeV2(bool,string,int) | 1321 | FAIL | 4 callsites, reorder [2,0,1] correct, mixed-named normalized — BUT rewrites the forwarding call inside SummarizeV2's own body → `SummarizeV2(verbose, title, count)` = infinite recursion, compiles clean (BulkRefactoringService.cs:235/312 — no exclusion of refs inside newMethod) |
| 123 | 6k.6 | bulk_replace_type_apply | token #122 | 3 | PASS | 1 file = preview (route accepted) |
| 124 | 6k.6 | compile_check | file=G4Triangle, Warning | 1358 | FLAG | 0 diagnostics — infinite recursion undetected |
| 125 | 6k.6 | revert_last_apply | | 1482 | PASS | restored |
| 126 | 6k.6 | preview_record_field_addition | SampleLib.G4Point + Z:int=0 | 81 | PASS | 1 positional construction site, suggested `(0, 0, 0)`; read-only audit (no token) |
| 127 | 6k.6 | record_field_add_with_satellites_preview | G4Point + Z | 7 | PASS | empty + actionable patternDetectionReason (negative path) |
| 128 | 6k.6 | create_file_preview/apply | SampleLib/G4Counters.cs (Hits/Misses mirrored in Clone/Snapshot/Reset) | 8/391 | PASS | |
| 129 | 6k.6 | record_field_add_with_satellites_preview | G4Counters + Evictions:int | 1414 | FAIL | only CloneMethodBody detected (Snapshot tuple + Reset missed); proposed edit inserts `    Evictions = this.Evictions,` at line 9 col 1 — OUTSIDE the single-line object initializer on line 8; does not add the field itself |
| 130 | 6k.6 | apply_composite_preview | token #129 | 1539 | FAIL | applied; compile_check → CS1519 ×4 (2 unique, each listed twice) |
| 131 | 6k.6 | compile_check | Error, limit 5 | 57 | FLAG | syntax diagnostics duplicated (4 = 2×2); earlier semantic errors not duplicated — observation, not source-confirmed |
| 132 | 6k.6 | revert_last_apply | | 3147 | FAIL | reverted seq 36 "Create file 'G4Counters.cs'" (deleted file) instead of the latest apply (seq 37 apply_composite_preview) — CompositeApplyOrchestrator never calls UndoService.CaptureBeforeApply (only ChangeTracker.RecordChange CompositeApplyOrchestrator.cs:117). Also contradicts description "Does not remove created files" |
| 133 | 6m | workspace_changes | | 17 | FLAG | 37 entries ordered; rolled-back (7,28,33) and reverted (31,35,36) entries indistinguishable from live ones; revert ops not logged; mixed path separators; add_pragma logged as "Apply text edit" (IChangeTracker.cs:17 has no status API) |
| 134 | 6k.6 | extract_shared_expression_to_helper_preview | SharedExpressionProbe 18:13-18:104 → NormalizePath | 29 | PASS | 2 sites + private static helper. FLAG: description names no apply route (ExtractMethodTools.cs:72). Preview-only (fixture used by repo tests). Fixture comment stale ("Lines 14-15", `src/RoslynMcp.Tests/…`) |
| 135 | 6k.6 | parameter_object_preview | G4ShapeReport.Summarize [title,count] → SummaryRequest | 52 | PASS | 4 call sites incl. mixed-named; new record file |
| 136 | 6k.6 | apply_with_verify | token #135 (route named in description) | 452 | PASS | status=applied, 2 files (SummaryRequest.cs new, G4Triangle.cs) |
| 137 | 6k.6 | split_service_with_di_preview | G4Fixture → G4Counter[Increment,Decrement] + G4Describer[Lookup,Describe] | 1516 | FAIL (Critical-on-apply) | warning "No DI registration" OK, but facade G4Fixture.cs rewrite DELETES every sibling type in the file (IG4Shape, G4Square, G4Circle, G4Point, G4Consumer), drops `: IG4Fixture`, the const, `_log`; partitions reference AppendLog/BuildLine they don't contain; G4Counter gets bogus `int counter` ctor param. SymbolRefactorService.cs:843-849 builds a fresh CompilationUnit with only `ClassDeclaration(sourceType)`. NOT applied (composite apply has no undo; file untracked) |
| 138 | 6m | workspace_changes | | 0 | FLAG | 38 entries, seq 38 = apply_with_verify parameter object; ordering correct |
| 139 | 6m | compile_check | Warning | 13 | PASS | 0 errors; CS8603 (from extract_method #84) + CS0169 remain |
| 140 | 7 | workspace_list | | n/a | PASS | W_RW v65, 42 docs |
| 141 | 7 | get_editorconfig_options | Cat.cs | 8 | PASS | 41 keys match root .editorconfig; every entry source="disk" (none from Roslyn snapshot) |
| 142 | 7 | set_editorconfig_option | dotnet_separate_import_directive_groups=true (existing [*.cs] key) | 1 | PASS | updated in place (diff: 1 line), no duplicate |
| 143 | 7 | get_editorconfig_options | Cat.cs | 5 | PASS | value now true |
| 144 | 7 | (revert) | cp pre-Phase-7 backup → .editorconfig | n/a | PASS | md5 190723982b2f… = pre-Phase-7; remaining +3 lines = Phase 6 set_diagnostic_severity (kept) |
| 145 | 7b | get_msbuild_properties | SampleLib, includedNames[6] | 71 | PASS | TF=net10.0, RootNamespace=SampleLib, OutputType=Library, Nullable=enable, TWAE=false, LangVersion=latest; totalCount 748 |
| 146 | 7b | evaluate_msbuild_property | TargetFramework | 1 | PASS | net10.0 — consistent |
| 147 | 7b | evaluate_msbuild_items | Compile | 0 | PASS | 27 items incl. new G4*/SummaryRequest files — consistent with workspace |

## Final state
- RW `git status --porcelain`: ` M .editorconfig` (Phase 6 CA1861 only), ` M SampleLib/Cat.cs`, ` M SampleLib/DiagnosticsProbe.cs` (CS0414 now pragma-suppressed — fixture used by repo integration tests), `?? G4CacheStore.cs G4Fixture.cs G4Triangle.cs IG4Fixture.cs SummaryRequest.cs`.
- Primary checkout: only pre-existing `?? .audit-state.json`.
- W_RW compiles with 0 errors (2 warnings: CS8603, CS0169).
- Side note: worktree root also tracks a duplicate SampleApp/SampleLib/SampleLib.Tests/SampleSolution.slnx tree beside samples/SampleSolution (repo-shape oddity, not a server bug).
