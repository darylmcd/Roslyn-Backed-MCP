# Phase G6 evidence — Phases 10, 12, 13 (W_RW 4b02621ce1f44d3eadad0966849206b1)

Paths: `<rw>` = `<repo>/.worktrees/surface-test-20260924T130517Z`, `<sl>` = `<rw>/samples/SampleSolution/SampleLib`.
Operator stderr access: no.

## git status --porcelain (start)
```
 M .editorconfig
 M samples/SampleSolution/SampleLib/Cat.cs
 M samples/SampleSolution/SampleLib/DiagnosticsProbe.cs
?? samples/SampleSolution/SampleLib/G4CacheStore.cs
?? samples/SampleSolution/SampleLib/G4Fixture.cs
?? samples/SampleSolution/SampleLib/G4Triangle.cs
?? samples/SampleSolution/SampleLib/IG4Fixture.cs
?? samples/SampleSolution/SampleLib/SummaryRequest.cs
```
## git status --porcelain (end)
Same as start PLUS `?? samples/SampleSolution/SampleLib.Tests/DogGeneratedTests.cs` (kept on purpose — scaffolded test, runs green). compile_check end: 0 errors.

## Phase 10 — file / cross-project / orchestration

| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 1 | workspace_list | — | n/a | PASS | W_RW v65, 3 proj, 42 docs, ready |
| 2 | move_type_to_file_preview | BacklogSamples.cs / BacklogAsyncSample | 18 | PASS | 2-file diff; new file has no blank line after `namespace` |
| 3 | move_type_to_file_apply | token | 429 | FLAG | on-disk = preview (2 files). New file written UTF-8 **BOM + CRLF**; repo `.editorconfig` end_of_line=lf, charset=utf-8; source file LF. Source left trailing blank line |
| 4 | compile_check | sev=Error | 1937 (queued 1505 auto-reload) | PASS | 0 errors |
| 5 | move_file_preview | → Moved/BacklogAsyncSample.cs, updateNamespace=true | 16 | PASS | ns → SampleLib.Moved; warning "Namespace references outside the moved file are not automatically rewritten" (no refs existed) |
| 6 | move_file_apply | token | 425 | PASS | disk = preview |
| 7 | create_file_preview | G6Created.cs | 1391 | PASS | preview did not write file (verified) |
| 8 | create_file_apply | token | 654 | FLAG | file written with UTF-8 BOM (content LF preserved) — contradicts SourceFileEncoding new-file default (no BOM) |
| 9 | delete_file_preview | G6Created.cs | 1218 | PASS | |
| 10 | delete_file_apply | token | 392 | PASS | file gone |
| 11 | move_type_to_file_preview (neg) | Dog.cs single type | 1546 | PASS | actionable: "Source file contains only one top-level type. ... use move_file_preview" |
| 12 | delete_file_preview (neg) | DoesNotExist.cs | 1827 | FLAG | vague: "operation is not valid for the current workspace state. Call workspace_reload..." (reloadConfirmedNotFound=true in _meta but message redacted) |
| 13 | create_file_preview (neg) | existing Dog.cs | 6 | FLAG | vague generic InvalidOperation (redacted) |
| 14 | extract_interface_cross_project_preview | AnimalService → SampleApp | 90 | FLAG | vague generic; real cause = cycle msg at CrossProjectRefactoringService.cs:764-765, redacted by ToolErrorHandler.cs:152-171 |
| 15 | dependency_inversion_preview | AnimalService, interfaceProject=SampleLib | 24 | PASS | IAnimalService + base list; no ctor consumers exist |
| 16 | apply_composite_preview | dependency_inversion token | 0 | FAIL | PreviewTokenStale "workspace was reloaded after the preview was created" — false; token lives in IPreviewStore, composite store lookup misses |
| 17 | workspace_list | — | n/a | PASS | v74 |
| 18 | dependency_inversion_preview (re) | same | 10 | PASS | |
| 19 | apply_composite_preview | fresh token, immediately | 0 | FAIL | same false "stale" |
| 20 | preview_multi_file_edit_apply | same token | 506 | PASS | applied; disk = preview (2 files); undocumented route |
| 21 | compile_check | | 1712 | PASS | 0 errors → reverted via git |
| 22 | move_type_to_project_preview | TrulyUnusedConcreteType ConventionFixtures.cs → SampleApp | 1601 | FLAG | vague generic. Service always adds source→target ProjectReference (CrossProjectRefactoringService.cs:94) → cycle since SampleApp→SampleLib; unreachable in this fixture. Target file name = source file name (line 74), not type name |
| 23 | extract_interface_cross_project_preview | target=SampleLib (same project), interfaceName=IAnimalSvc | 22 | FLAG | accepted same-project target silently though description says use extract_interface_preview for same-project |
| 24 | get_di_registrations | summary=true | 41 | PASS | count 0 → no DI in fixture |
| 25 | extract_and_wire_interface_preview | AnimalService, target SampleLib | 17 | FLAG | works but no warning that 0 DI registrations found/rewired (updateDiRegistrations=true default) |
| 26 | apply_composite_preview | extract_and_wire token | 1541 | PASS | disk = preview (2 files). New file CRLF, **no BOM** (differs from other apply paths) |
| 27 | compile_check | | 63 | PASS | |
| 28 | workspace_changes | | 0 | PASS | 45 entries incl. G4's; seq44 recorded dependency_inversion as preview_multi_file_edit_apply |
| 29 | split_class_preview | Dog, [Fetch], Dog.Behavior.cs | 1519 | PASS/FLAG | valid partials; cosmetic stray blank lines; response filePath mixes `/` (Dog.cs) and `\` (new file) |
| 30 | apply_composite_preview | split token | 1397 | PASS | disk = preview; appliedFiles also mixed separators |
| 31 | compile_check | | 56 | PASS | → reverted |
| 32 | migrate_package_preview | MSTest.TestAdapter → MSTest.TestAdapter.Renamed 9.9.9 | 1547 | FLAG | CPM-aware (edits csproj + Directory.Packages.props) BUT removes central `PackageVersion MSTest.TestAdapter` from the shared repo-root props used by 3 projects outside the loaded workspace (tests/RoslynMcp.Tests, tests/RoslynMcp.ShardDiscoveryFixtures, root SampleLib.Tests) — no warning. Not applied |
| 33 | migrate_package_preview (neg) | No.Such.Package | 1 | FLAG | vague generic (redacted) |
| 34 | ToolSearch `+apply_composite` | | n/a | PASS | only `apply_composite_preview` present in 4.2.1; `apply_composite` absent (HEAD adds it as canonical, ServerSurfaceCatalog.Orchestration.cs / OrchestrationTools.cs) |

## Phase 12 — scaffolding

| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 35 | scaffold_type_preview | G6Widget, interfaces=[IAnimal] | 53 | PASS | `internal sealed class`, stubs w/ NotImplementedException + `using System;` |
| 36 | scaffold_type_preview | implementInterface=false, namespace=SampleLib.G6 | 9 | PASS | empty body; file at SampleLib/G6/G6Widget.cs. (Would not compile: CS0535 — no warning) |
| 37 | scaffold_type_preview | typeKind=enum | 6 | PASS | `public enum` |
| 38 | scaffold_type_preview (neg) | typeKind=struct | 0 | FLAG | vague: "Parameter '<unknown>' is invalid" (ArgumentException redaction) |
| 39 | scaffold_type_apply | token #35 | 411 | FLAG | content = preview, but file has **mixed EOL**: LF lines + 2 CRLF blank separator lines, plus BOM |
| 40 | compile_check | | 1673 | PASS | 0 errors → file removed |
| 41 | scaffold_test_preview | Dog / Speak | 1642 | PASS | MSTest detected; `new Dog()` |
| 42 | scaffold_test_apply | token | 517 | PASS | disk = preview; pure LF |
| 43 | test_discover | nameFilter=DogGenerated | 1364 | PASS | 1 test found |
| 44 | test_run | filter DogGeneratedTests, compact | 2229 | PASS | 1/1 passed — KEPT |
| 45 | scaffold_test_batch_preview | AnimalRecord, RefactoringProbe, MultiNamespaceService | 49 | PASS | ONE token, 3 files; ctor args string.Empty / default(int) / Array.Empty<string>() / new StringBuilder() / default(TextWriter)! |
| 46 | apply_composite_preview | batch token (per tool description) | 0 | FAIL | false PreviewTokenStale; tool description says "apply via apply_composite_preview" but token is in IPreviewStore |
| 47 | preview_multi_file_edit_apply | batch token | 514 | PASS | 3 files = preview |
| 48 | compile_check | SampleLib.Tests, sev=Warning | 1524 | PASS | 0 diag |
| 49 | test_discover | nameFilter=GeneratedTests | 0 | PASS | all 4 discoverable → 3 batch files removed |
| 50 | scaffold_first_test_file_preview | SampleLib.SharedExpressionProbe | 1453 | PASS | sealed class, ClassInitialize, 2 smoke tests |
| 51 | scaffold_first_test_file_preview (neg) | SampleLib.AnimalService (tests exist) | 1781 | FLAG | vague generic (description promises "Errors when the destination file already exists") |

## Phase 13 — project mutation

| # | Tool | Inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|
| 52 | add_package_reference_preview | SampleLib + Nito.AsyncEx 5.1.2 | 70 | PASS | CPM-aware: no Version attr |
| 53 | remove_package_reference_preview | Tests − MSTest.TestAdapter | 3 | PASS | |
| 54 | add_project_reference_preview (neg) | SampleLib → SampleApp (cycle) | 1 | FLAG | vague (real msg ProjectMutationService.cs:660-661 redacted) |
| 55 | add_package_reference_preview | Humanizer.Core (not in CPM) | 1 | PASS | actionable warning "Add 'Humanizer.Core' to Directory.Packages.props..." |
| 56 | add_project_reference_preview (neg) | SampleApp → SampleLib (dup) | 1 | FLAG | vague (ProjectMutationService.cs:149 redacted) |
| 57 | remove_project_reference_preview | SampleApp − SampleLib | 2 | FLAG | valid diff; no warning that Program.cs consumes SampleLib (would break compile) |
| 58 | add_project_reference_preview | Tests → SampleApp | 0 | PASS | |
| 59 | set_project_property_preview | LangVersion=13.0 | 2 | PASS | |
| 60 | set_project_property_preview (neg) | OutputPath | 0 | FLAG | vague; real msg lists allowlist (ProjectMutationService.cs:674-675) — redacted |
| 61 | set_conditional_property_preview | DefineConstants, Configuration==Debug | 4 | PASS | correct conditional PropertyGroup |
| 62 | set_conditional_property_preview (neg) | $(OS) condition | 0 | FLAG | vague; real msg ProjectMutationService.cs:683-684 redacted |
| 63 | add_target_framework_preview | SampleLib net8.0 | 3 | PASS | TargetFramework→TargetFrameworks net10.0;net8.0 |
| 64 | remove_target_framework_preview (neg) | only TF net10.0 | 3 | FLAG | vague; real msg ProjectMutationService.cs:392/409 redacted |
| 65 | add_target_framework_preview | Tests net9.0 (TF inherited from Directory.Build.props) | 66 | FLAG | adds separate PropertyGroup w/ TargetFrameworks; no warning that referenced SampleLib is net10.0-only |
| 66 | apply_project_mutation | token #65 | 1376 | PASS | disk = preview |
| 67 | get_msbuild_properties | includedNames=[TargetFramework,TargetFrameworks,IsCrossTargetingBuild,ManagePackageVersionsCentrally] | 75 | PASS | 3 of 4 returned; appliedFilter echoed |
| 68 | build_project | SampleLib.Tests | 1228 | FAIL | exitCode 1, NU1201 in stdout, but `errorCount:0, diagnostics:[]` |
| 69 | workspace_reload | verbose=false | 2730 | FLAG | after git revert: workspaceErrorCount=1, isReady=false (stale assets NU1201), `restoreRequired:false`, restoreHint null |
| 70 | workspace_list | verbose | n/a | PASS | WORKSPACE_FAILURE diag shows NU1201 |
| 71 | build_project | SampleLib.Tests | 1434 | PASS | restored; MSTEST0032 warning parsed (location null while CS* have location — contract inconsistency) |
| 72 | workspace_reload | | 1401 | PASS | ready, 0 errors |
| 73 | add_project_reference_preview | Tests → SampleApp | 0 | PASS | |
| 74 | workspace_fork_apply | project-mutation token, drop-always | 3 | FAIL | false PreviewTokenStale; description says "Preview token from any *_preview tool" but fork only reads IPreviewStore (WorkspaceForkApplyService.cs:169-175) |
| 75 | create_file_preview | G6Fork.cs | 8 | PASS | |
| 76 | workspace_fork_apply | create token, drop-always, forkName g6probe | 3081 | PASS | fork compile clean, source untouched; leaves empty `samples/SampleSolution/.roslynmcp/forks/` dir (removed manually); `discoveredTests:[]` yet dotnetTestFilter lists 6 tests |
| 77 | create_file_apply | same token after fork | 406 | PASS | token NOT consumed by fork (good) → file removed |
| 78 | add_project_reference_preview | Tests → SampleApp | 1391 | PASS | |
| 79 | apply_project_mutation | forward | 1367 | PASS | disk = preview |
| 80 | workspace_reload | | 1440 | PASS | |
| 81 | project_graph | | 0 | PASS | Tests refs [SampleLib, SampleApp] |
| 82 | build_project | SampleLib.Tests | 2588 | PASS | exit 0 |
| 83 | remove_project_reference_preview | reverse | 1 | PASS | |
| 84 | apply_project_mutation | reverse | 1471 | PASS | csproj byte-identical to HEAD (git clean) |
| 85 | add_central_package_version_preview | Humanizer.Core 2.14.1 | 2 | PASS | appended to last ItemGroup |
| 86 | remove_central_package_version_preview | Nito.AsyncEx | 3 | FLAG | removes shared entry consumed by src/RoslynMcp.Roslyn (outside workspace), no warning |
| 87 | add_central_package_version_preview (neg) | DiffPlex (exists) | 1 | FLAG | vague (ProjectMutationService.cs:485 redacted) |
| 88 | get_msbuild_properties | propertyNameFilter=Nullable | 71 | PASS | 1 of 748 |
| 89 | compile_check | final | 69 | PASS | 0 errors |

Observed pattern (not new): every *_apply marks workspace stale (WorkspaceManager.cs:1041) → next call auto-reloads (~1.4–1.8 s queuedMs). apply_composite_preview / apply_project_mutation did not trigger this on the following call in some cases.

## Reverts performed (git -C <rw> checkout/rm)
AnimalService.cs, BacklogSamples.cs, Dog.cs, SampleLib.Tests.csproj (checkout); removed IAnimalService.cs, Moved/BacklogAsyncSample.cs (+dir), Dog.Behavior.cs, G6Widget.cs, G6Fork.cs, AnimalRecordGeneratedTests.cs, RefactoringProbeGeneratedTests.cs, MultiNamespaceServiceGeneratedTests.cs, `.roslynmcp/forks`. Kept DogGeneratedTests.cs.
