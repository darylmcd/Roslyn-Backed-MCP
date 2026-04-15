# Experimental Promotion Exercise Report (PARTIAL)

> **Run status:** Phase 1 partially executed; Phases 2–11 not exercised. Exercise was stopped intentionally after Phase 1 surfaced three high-severity regressions. This report is the canonical hand-off for the next agent — all findings below are supported by reproduction steps, file paths, and expected/actual contrasts so the next agent can re-run each probe deterministically without re-reading this session.

## 1. Header
- **Date:** 2026-04-15T14:04:03Z
- **Audited solution:** ITChatBot.sln
- **Audited revision:** worktree branch `worktree-experimental-promotion-2026-04-15` from `main` (commit `ee167149723cc2392c43715312f93371a631e063`)
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\.claude\worktrees\experimental-promotion-2026-04-15\ITChatBot.sln`
- **Audit mode:** `full-surface` — disposable worktree used for preview/apply round-trips
- **Isolation:** Worktree at `.claude/worktrees/experimental-promotion-2026-04-15` (branch `worktree-experimental-promotion-2026-04-15`) — cleanup performed at end of session.
- **Client:** Claude Code with native Roslyn MCP binding (`mcp__roslyn__*` tools)
- **Workspace id:** `3c757fc4ef5e49edbbce374bdf665a88`
- **Server:** `roslyn-mcp 1.18.0+172d3c5118178eae2664768ced630b38d9418309`
- **Catalog version:** 2026.04
- **Experimental surface (from `server_info`):** 40 experimental tools, 20 experimental prompts, 1 experimental resource
- **Scale:** 35 projects, 801 documents (post-restore reload)
- **Repo shape:** Multi-project .NET 10 solution (`net10.0`). `.editorconfig` at repo root + `tests/`. `Directory.Build.props` present. **No Central Package Management** (`Directory.Packages.props` absent). 17 test projects (xUnit). Analyzers enabled via `Directory.Build.props`. No source generators detected. No multi-targeting.
- **Prior issue source:** `ai_docs/backlog.md` + prior audit at `ai_docs/audit-reports/20260413T180545Z_itchatbot_experimental-promotion.md` (v1.12.0)

> **Phase 0 drift check:** `server_info` (40 exp tools / 20 exp prompts / 1 exp resource) agreed exactly with `roslyn://server/catalog` and with the prompt appendix for v1.18.0. No surface drift.

### Partial-run caveats

- Phases 2–11 were not executed. Scorecard in §8 only contains entries for the tools actually exercised in Phase 1.
- Workspace cleanup: all Phase-1 mutations were reverted via `git checkout` + `rm` of the new `IDatabaseOptions.cs` (see Bug 9.1 — `revert_last_apply` does not undo project-file mutations or file creation).
- Phase 1 encountered repeated validation friction that made continued exercise unwise before the core `extract_*_apply` bugs are addressed (see §9).

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 1a | 1099 / 8 / 82 / 1099 | Tried `scope=solution`/`project`/`document`, diagnostic IDs `IDE0130`, `IDE0290`, `IDE0008`, `xUnit1051`, `CA1826`. Info-level IDs silently returned `fixedCount:0` with `guidanceMessage:null`; Warning-level IDs produced actionable guidance (`"No code fix provider is loaded for diagnostic 'CA1826'..."`). No code-fix-provider assembly present in this repo → apply path not reachable. See §5 & §9.5 for the silent-empty-guidance finding. |
| tool | `fix_all_apply` | refactoring | blocked | 1a | – | Blocked by `fix_all_preview` (no code fix provider loaded). |
| tool | `format_check` | refactoring | exercised | 1b | 775 (all docs) / 109 (one project) | Clean workspace → 0 violations across 642 docs in 775 ms. Within budget. |
| tool | `format_range_apply` | refactoring | exercised-apply | 1b | 7 | Round-trip clean. Introduced 9-space indent on DatabaseOptions.cs line 14 via `apply_text_edit`, `format_range_preview` returned token `b95d56c217a0420eadb711fc092ea6d9`, `format_range_apply` restored the canonical 4-space indent. |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 912 | Extracted `IDatabaseOptions` from `DatabaseOptions` with 2 members — preview diff was correct and reported both files. |
| tool | `extract_interface_apply` | refactoring | exercised-apply-FAIL | 1c | 456 | **BUG 9.1** — silently mutated `ITChatBot.Configuration.csproj` by adding `<ItemGroup><Compile Include="IDatabaseOptions.cs" /></ItemGroup>`, which then caused MSBuild `Duplicate 'Compile' items` failure on `workspace_reload`. |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 2 (refused) / 8 (success) | Refused `AddEvidenceLinksAsync` extraction from `InMemoryConversationRepository` with good actionable message (§9.4). Successfully previewed `Down` extraction from `RemoveConversationExpiresAtUtc`. |
| tool | `extract_type_apply` | refactoring | exercised-apply-FAIL | 1d | 474 | **BUGS 9.1 + 9.2 + 9.3** — same csproj mutation bug as `extract_interface_apply`, plus duplicate file write to wrong directory, plus preserved `override` modifier on new type without base. |
| tool | `move_type_to_file_preview` | refactoring | exercised-preview-only | 1e | 3 | Correctly refused with actionable error: "Source file only contains one top-level type. Nested types cannot be extracted with this tool — only top-level type declarations are considered." (This is a repo-shape limit, not a defect.) |
| tool | `move_type_to_file_apply` | refactoring | needs-more-evidence | 1e | – | Blocked; no multi-type file located in time to retry. |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 1108 | Exercised `scope=parameters` (non-default). Correctly rewrote parameter type in `DatabaseOptionsValidator.Validate` but preserved class-level `IValidateOptions<DatabaseOptions>` — which *would* cause the compile to fail under the exact-match interface rule. See §9.6 for the actual compile result. |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply-FAIL | 1f | 4 | Applied preview; the resulting file no longer satisfies `IValidateOptions<DatabaseOptions>` because only the method parameter was swapped to `IDatabaseOptions`, not the interface generic. Workspace was reverted via `git checkout`. |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 2 (refused twice) / 16 (success) | Refused two selections in `SynthesisPromptAssembler.TrimMessagesToContentBudget` with "All selected statements must be in the same block scope" (good actionable error). Succeeded on lines 42-46 of `AmbiguityDetector.Detect`. |
| tool | `extract_method_apply` | refactoring | exercised-apply | 1g | 6 | Round-trip clean — `compile_check` = 0 errors, `find_references` found the new callsite. **But** preview diff contained formatting defects: missing spaces around `=` at the new callsite, new method declared at column 1 (no class-scope indent), and the multi-line LINQ chain was collapsed to a single long line. See §9.7. |
| tool | `restructure_preview` (v1.17) | refactoring | needs-more-evidence | 1h | – | Not exercised. |
| tool | `replace_string_literals_preview` (v1.17) | refactoring | needs-more-evidence | 1i | – | Not exercised. |
| tool | `change_signature_preview` (v1.18) | refactoring | needs-more-evidence | 1j | – | Not exercised. |
| tool | `symbol_refactor_preview` (v1.18) | refactoring | needs-more-evidence | 1k | – | Not exercised. |
| tool | `create_file_apply` | file-operations | needs-more-evidence | 2a | – | Not exercised. |
| tool | `move_file_apply` | file-operations | needs-more-evidence | 2b | – | Not exercised. |
| tool | `delete_file_apply` | file-operations | needs-more-evidence | 2c | – | Not exercised. |
| tool | `add_central_package_version_preview` | project-mutation | needs-more-evidence | 3e | – | Would have been `skipped-repo-shape` (no `Directory.Packages.props`). |
| tool | `apply_project_mutation` | project-mutation | needs-more-evidence | 3a | – | Not exercised. |
| tool | `scaffold_type_preview` | scaffolding | needs-more-evidence | 4a | – | Not exercised. |
| tool | `scaffold_type_apply` | scaffolding | needs-more-evidence | 4a | – | Not exercised. |
| tool | `scaffold_test_apply` | scaffolding | needs-more-evidence | 4b | – | Not exercised. |
| tool | `scaffold_test_batch_preview` (v1.17) | scaffolding | needs-more-evidence | 4c | – | Not exercised. |
| tool | `remove_dead_code_apply` | dead-code | needs-more-evidence | 5a | – | Not exercised. |
| tool | `remove_interface_member_preview` (v1.15) | dead-code | needs-more-evidence | 5a | – | Not exercised. |
| tool | `apply_multi_file_edit` | editing | needs-more-evidence | 5b | – | Not exercised. |
| tool | `preview_multi_file_edit` (v1.17) | editing | needs-more-evidence | 5b-i | – | Not exercised. |
| tool | `preview_multi_file_edit_apply` (v1.17) | editing | needs-more-evidence | 5b-i | – | Not exercised. |
| tool | `apply_with_verify` (v1.15) | undo | needs-more-evidence | 5e | – | Not exercised. |
| tool | `move_type_to_project_preview` | cross-project-refactoring | needs-more-evidence | 6 | – | Not exercised. |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | needs-more-evidence | 6 | – | Not exercised. |
| tool | `dependency_inversion_preview` | cross-project-refactoring | needs-more-evidence | 6 | – | Not exercised. |
| tool | `migrate_package_preview` | orchestration | needs-more-evidence | 7 | – | Not exercised. |
| tool | `split_class_preview` | orchestration | needs-more-evidence | 7 | – | Not exercised. |
| tool | `extract_and_wire_interface_preview` | orchestration | needs-more-evidence | 7 | – | Not exercised. |
| tool | `apply_composite_preview` | orchestration | needs-more-evidence | 7 | – | Not exercised. |
| tool | `symbol_impact_sweep` (v1.17) | analysis | needs-more-evidence | 7c.1 | – | Not exercised. |
| tool | `test_reference_map` (v1.17) | validation | needs-more-evidence | 7c.2 | – | Not exercised. |
| tool | `validate_workspace` (v1.18) | validation | needs-more-evidence | 7c.3 | – | Not exercised. |
| tool | `get_prompt_text` (v1.18) | prompts | needs-more-evidence | 7c.4 | – | Not exercised. |
| resource | `roslyn://workspace/{id}/file/{path}/lines/{start}-{end}` | resource | needs-more-evidence | 7b | – | Not exercised. |
| prompt | (all 20 experimental prompts) | prompts | needs-more-evidence | 8 | – | Not exercised. |

**Coverage summary:** exercised/exercised-apply 7 tools; exercised-preview-only 2 tools; exercised-apply-FAIL 3 tools; blocked 1 tool; needs-more-evidence 27 tools + 1 resource + 20 prompts.

## 3. Performance baseline (`_meta.elapsedMs`)

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `fix_all_preview` | refactoring | 5 | 8 | 1099 | 1 document / 1 project / 1 solution | ≤15s | Well within budget. |
| `format_check` | refactoring | 2 | 442 | 775 | 8 docs / 642 docs | ≤15s | Well within budget. |
| `format_range_preview` | refactoring | 1 | 6381 | 6381 | single line | ≤5s | **FLAG** — included ~6345 ms of `staleAction: "auto-reloaded"` overhead after `apply_text_edit`. Actual in-service time was ~35 ms. |
| `format_range_apply` | refactoring | 1 | 7 | 7 | 1 file | ≤30s | Fast. |
| `extract_interface_preview` | refactoring | 1 | 912 | 912 | 2 members | ≤5s | Within budget. |
| `extract_interface_apply` | refactoring | 1 | 456 | 456 | 2 files | ≤30s | Within budget — but produces invalid csproj (Bug 9.1). |
| `extract_type_preview` | refactoring | 3 | 2 | 8 | 1–2 members | ≤5s | Fast. |
| `extract_type_apply` | refactoring | 1 | 474 | 474 | 2 files | ≤30s | Within budget — but three defects (Bugs 9.1, 9.2, 9.3). |
| `bulk_replace_type_preview` | refactoring | 1 | 1108 | 1108 | 1 file touched | ≤15s | Within budget. |
| `bulk_replace_type_apply` | refactoring | 1 | 4 | 4 | 1 file | ≤30s | Fast — but produces code that fails `IValidateOptions<T>` exact-match (Bug 9.6). |
| `extract_method_preview` | refactoring | 3 | 2 | 16 | 13–93 LoC | ≤5s | Fast. |
| `extract_method_apply` | refactoring | 1 | 6 | 6 | 1 file | ≤30s | Fast; correct semantically, but cosmetic defects in output (Bug 9.7). |
| `move_type_to_file_preview` | refactoring | 1 | 3 | 3 | 1 file | ≤5s | Fast (refused). |
| `revert_last_apply` | undo (stable) | 1 | 6709 | 6709 | 1 applied op | ≤30s | Stable tool; included here because the Phase 1 cleanup required it. |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `extract_interface_apply` | Side-effect not documented | Per tool description "Apply a previously previewed interface extraction". Nothing says the tool also mutates `.csproj`. | Writes `<ItemGroup><Compile Include="…" /></ItemGroup>` to the project file. | **High** | The preview diff does not show the csproj change either (see §9.1). |
| `extract_type_apply` | Side-effect not documented | Per description "Apply a previously previewed type extraction". | Same csproj mutation as above, and writes the extracted-type file to two locations (see §9.1, §9.2). | **High** | |
| `extract_interface_apply` / `extract_type_apply` | Preview diff incomplete | `changes[].unifiedDiff` should enumerate every file that will change | csproj mutation missing from the preview diff; file-duplication target folder missing from preview diff | **High** | An agent cannot verify the full effect from the preview alone. |
| `extract_type_apply` response | `appliedFiles` incomplete | `appliedFiles` should list every touched file | Reported 2 `appliedFiles` (the two `.cs` files) but not the csproj, and pointed at the in-subfolder path even though the tool also wrote a second copy at the parent-folder path | **High** | Side-effects invisible to caller. |
| `extract_method_preview` | Preview produces non-idiomatic code | Output should respect project `.editorconfig` formatting | Call site `var words=NormalizeQuestionWords(questionText);` (missing whitespace around `=`), new method at column 1 (no class-scope indent), multi-line LINQ collapsed to one line | **Medium** | Output compiles but fails style checks (see §9.7). |
| `fix_all_preview` | Inconsistent guidance | On Info-severity diagnostics with no code fix provider | Returns empty `{ previewToken:"", fixedCount:0, changes:[], guidanceMessage:null }` — silent | **Medium** | On Warning-severity IDs like `CA1826` / `xUnit1051`, it returns a helpful guidanceMessage pointing at `list_analyzers`. The silent path is confusing; agents will assume "0 occurrences" rather than "no fix provider loaded". See §9.5. |

## 5. Error message quality

> Phase 9 negative-probe battery not run. The table below contains the negative paths actually observed during Phase 1.

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `fix_all_preview` | `diagnosticId="xUnit1051"` (no fix provider loaded) | **actionable** | — | `"No code fix provider is loaded for diagnostic 'xUnit1051'. Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs."` |
| `fix_all_preview` | `diagnosticId="CA1826"` (no fix provider loaded) | **actionable** | — | Same message template with `CA1826`. |
| `fix_all_preview` | `diagnosticId="IDE0130"` / `IDE0290` / `IDE0008` (Info-severity) | **unhelpful** | Emit the same `"No code fix provider is loaded…"` guidance when the diagnostic has occurrences but no provider, regardless of severity — or distinguish "no matches" from "no fix provider" in the response | Empty token, `fixedCount:0`, `guidanceMessage:null` — the caller cannot tell whether the diagnostic has no occurrences or whether the fix provider is missing. See §9.5. |
| `extract_type_preview` | `memberNames=["AddEvidenceLinksAsync"]` references a field that would stay on the source | **actionable** | — | Explicit list of referenced members and a remediation hint. Great error. |
| `extract_type_preview` | Same input but with `memberNames=["AddEvidenceLinksAsync", "_evidenceLinks"]` — i.e. including the field | **vague** | Accept fields in `memberNames`, or error with "fields are not a supported member kind; refactor the field into a property before retrying" | Tool appears to ignore the field in `memberNames` and emits the same referenced-member error. The error message does not say fields are unsupported, and the schema for `memberNames` is just `string[]`. |
| `extract_method_preview` | Selection spans two block scopes | **actionable** | — | `"Invalid operation: All selected statements must be in the same block scope.."` — correct. (Stylistic: trailing double dot.) |
| `move_type_to_file_preview` | Source file has only one top-level type | **actionable** | — | Clear refusal with explanation. |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | `scope=solution` / `scope=project` / `scope=document` | **done** | All three paths exercised. |
| `format_check` | `projectName=ITChatBot.Configuration` (project scope vs. default solution scope) | **done** | Both paths exercised. |
| `bulk_replace_type_preview` | `scope=parameters` (non-default vs. `scope=all`) | **done** | Non-default exercised; apply path surfaced the interface-exact-match gap (§9.6). |
| `extract_type_preview` | `memberNames` containing a field name rather than only methods/properties | **done** (tested but behavior is vague — §5 row 5) | |
| `scaffold_type_preview` | `kind=record` / `kind=interface` + `implementInterface=false` | **not exercised** | |
| `get_msbuild_properties` | `propertyNameFilter` | **not exercised** | |
| File operations | move with `updateNamespace=true` | **not exercised** | |
| Project mutation | `set_conditional_property_preview`, CPM | **not exercised** | |

## 7. Prompt verification (Phase 8)

Not exercised. All 20 experimental prompts remain `needs-more-evidence`.

## 8. Experimental promotion scorecard

> Partial scorecard — only entries actually exercised in Phase 1. All other experimental entries default to **needs-more-evidence**.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 8 | partial | partial | n/a | §9.5 silent-guidance on Info-severity | **keep-experimental** | Good guidance on Warning-severity IDs; silent on Info. |
| tool | `fix_all_apply` | refactoring | blocked | – | unknown | unknown | – | blocked | **needs-more-evidence** | No code-fix provider reachable in this repo. |
| tool | `format_check` | refactoring | exercised | 442 | yes | n/a | n/a | none | **promote** | Clean run across 642 docs, no schema drift, p50 within budget. |
| tool | `format_range_apply` | refactoring | exercised-apply | 7 | yes | n/a | yes | none | **keep-experimental** | Round-trip clean; but `format_range_preview` (stable) emits large `staleReloadMs` overhead after `apply_text_edit` (§3 note). Round-trip limited to one small range probe. |
| tool | `extract_interface_preview` | refactoring | exercised | 912 | yes | n/a | n/a | none | **keep-experimental** | Preview diff is correct but *incomplete* — does not surface the csproj change that `*_apply` later produces (§9.1). |
| tool | `extract_interface_apply` | refactoring | exercised-apply-FAIL | 456 | **no** | n/a | **no** | §9.1 | **deprecate-or-fix** | Breaks SDK-style projects. Must not promote while the csproj mutation exists. |
| tool | `extract_type_preview` | refactoring | exercised | 5 | yes | yes | n/a | §9.4 UX follow-up on field-name input | **keep-experimental** | Refusal UX is excellent; minor schema/ux gap on field-name support. |
| tool | `extract_type_apply` | refactoring | exercised-apply-FAIL | 474 | **no** | n/a | **no** | §9.1 + §9.2 + §9.3 | **deprecate-or-fix** | Three distinct defects, all reproducible. |
| tool | `move_type_to_file_preview` | refactoring | exercised-preview-only | 3 | yes | yes | n/a | none | **keep-experimental** | Only observed via the refusal path. Needs a multi-type-file probe to meet promotion bar. |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1108 | yes | n/a | n/a | §9.6 (scope-semantics) | **keep-experimental** | Non-default path exercised; but the `scope=parameters` semantics needs clarification (does not rewrite generic args even when those are the interface contract). |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply-FAIL | 4 | partial | n/a | **no** | §9.6 | **keep-experimental** | Applied the preview faithfully; but the preview semantics left the workspace in a broken state (IValidateOptions<T> parameter-type mismatch). Not the apply tool's fault, but users need a warning. |
| tool | `extract_method_preview` | refactoring | exercised | 9 | partial | yes | n/a | §9.7 (formatting defects in output) | **keep-experimental** | Semantically correct; cosmetically wrong. |
| tool | `extract_method_apply` | refactoring | exercised-apply | 6 | partial | n/a | yes | §9.7 | **keep-experimental** | Round-trip compiles; output requires a follow-up `format_document_apply`. |

## 9. MCP server issues (bugs)

### 9.1 `extract_interface_apply` / `extract_type_apply` add `<Compile Include="…">` items that break SDK-style projects
| Field | Detail |
|-------|--------|
| Tool | `extract_interface_apply`, `extract_type_apply` |
| Reproduction A (extract_interface_apply) | 1. `workspace_load` any SDK-style `.csproj`. 2. `extract_interface_preview(typeName="DatabaseOptions", interfaceName="IDatabaseOptions", filePath=…\DatabaseOptions.cs)`. 3. Apply the token. 4. `workspace_reload`. |
| Reproduction B (extract_type_apply) | 1. Same workspace. 2. `extract_type_preview(typeName="RemoveConversationExpiresAtUtc", memberNames=["Down"], newTypeName="RemoveConversationExpiresAtUtcRollback", filePath=…\Migrations\20260413203000_RemoveConversationExpiresAtUtc.cs)`. 3. Apply the token. 4. `workspace_reload`. |
| Expected | Only the new `.cs` file and any modified source files are changed. Project file (`.csproj`) is untouched because the SDK's `EnableDefaultCompileItems=true` default globs `*.cs` automatically. |
| Actual | `.csproj` gets an appended `<ItemGroup><Compile Include="NewType.cs" /></ItemGroup>`. `workspace_reload` then reports `WORKSPACE_FAILURE` / MSBuild error: `"Duplicate 'Compile' items were included. The .NET SDK includes 'Compile' items from your project directory by default. You can either remove these items from your project file, or set the 'EnableDefaultCompileItems' property to 'false' if you want to explicitly include them in your project file. For more information, see https://aka.ms/sdkimplicititems. The duplicate items were: 'IDatabaseOptions.cs'"` |
| Observed from this run (verbatim) | See `workspace_reload` response, `workspaceDiagnostics[0]`, captured during the extract_interface_apply reproduction — duplicate-Compile diagnostic with `filePath: null`, `severity: "Error"`, category `"Workspace"`. |
| Side-effect: csproj also reformatted | Tool adds a leading UTF-8 BOM, strips blank lines between `<PropertyGroup>` / `<ItemGroup>` / `</Project>`, and drops the trailing newline. Captured via `git diff src/config/ITChatBot.Configuration.csproj` and `git diff src/data/ITChatBot.Data.csproj`. |
| Files reproduced against | `src/config/ITChatBot.Configuration.csproj`, `src/data/ITChatBot.Data.csproj` |
| Severity | **High** — makes apply unusable on the default SDK template shape for .NET 6+. |
| Reproducibility | 100% on this repo; 2 separate tools on 2 separate projects. |
| Suggested fix for next agent | (1) Detect SDK-style projects via `EnableDefaultCompileItems` property lookup (already accessible via `evaluate_msbuild_property`). When true, skip the `<Compile Include>` injection. (2) Also preserve csproj whitespace/BOM state — round-trip the file without cosmetic rewrites. (3) Surface the csproj change in the preview's `changes[].unifiedDiff`. (4) Add a post-apply `workspace_reload` smoke test for both tools in the MCP server's integration suite. |
| Related code locations to inspect | `src/tools/Refactoring/ExtractInterfaceTools.cs`, `src/tools/Refactoring/ExtractTypeTools.cs`, project-mutation helpers under `src/services/ProjectMutation/`. (Paths are educated guesses; the Roslyn-MCP server repo is not checked out here.) |

### 9.2 `extract_type_apply` writes the new type file to two locations
| Field | Detail |
|-------|--------|
| Tool | `extract_type_apply` |
| Reproduction | Same as 9.1 reproduction B. |
| Expected | One new file at the path declared by the preview diff: `src\data\Migrations\RemoveConversationExpiresAtUtcRollback.cs`. |
| Actual | **Two** files with identical contents: (a) `src\data\Migrations\RemoveConversationExpiresAtUtcRollback.cs` and (b) `src\data\RemoveConversationExpiresAtUtcRollback.cs` (parent folder). The csproj injection from 9.1 resolves against the csproj directory (`src\data\`) and therefore references the parent-folder copy. |
| Observed | `git status --short` after apply showed both `??` paths; `Glob "**/RemoveConversationExpiresAtUtcRollback.cs"` returned both. Contents read via `Read` were byte-identical. |
| Severity | **High** — silent duplication; later edits to only one copy desync the sources. |
| Reproducibility | 100% when the extracted type lives in a subfolder of the project. |
| Suggested fix for next agent | (1) Write the new file exactly at the preview's declared path and nowhere else. (2) If the csproj `<Compile Include>` injection survives (see 9.1), make the include path relative to the declared new-file path. (3) Add a regression test that extracts into a subfolder and asserts `Directory.EnumerateFiles(projectRoot, newTypeName + ".cs", SearchOption.AllDirectories).Count() == 1`. |

### 9.3 `extract_type_apply` preserves `override` when new type does not inherit the base
| Field | Detail |
|-------|--------|
| Tool | `extract_type_apply` |
| Reproduction | Same as 9.1 reproduction B (source type inherits `Migration` and member `Down` is declared `protected override`). |
| Expected | Either strip the `override` modifier (method is now a fresh top-level member of a non-inheriting class) or refuse with an actionable error (mirroring the `_evidenceLinks` refusal in §9.4). |
| Actual | New class emitted as `public sealed class RemoveConversationExpiresAtUtcRollback { public override void Down(MigrationBuilder migrationBuilder) { … } }`. Compile fails with `CS0115: 'RemoveConversationExpiresAtUtcRollback.Down(MigrationBuilder)': no suitable method found to override`. |
| Observed (verbatim from `compile_check`) | ``{ "id":"CS0115","message":"'RemoveConversationExpiresAtUtcRollback.Down(MigrationBuilder)': no suitable method found to override","severity":"Error","filePath":"…\\src\\data\\Migrations\\RemoveConversationExpiresAtUtcRollback.cs","startLine":6,"startColumn":26 }`` and a mirrored diagnostic for the parent-folder copy. |
| Severity | **Medium** — detectable at first `compile_check`, but the tool should either rewrite the modifier or refuse. |
| Suggested fix for next agent | When moving a member into a new top-level type with no base type, rewrite the member's modifier list: drop `override`/`virtual`/`abstract`, and keep `sealed` at class scope only. |

### 9.4 `extract_type_preview` correctly refuses when captured state would stay on the source (PASS)
| Field | Detail |
|-------|--------|
| Tool | `extract_type_preview` |
| Reproduction | 1. `workspace_load`. 2. `extract_type_preview(typeName="InMemoryConversationRepository", memberNames=["AddEvidenceLinksAsync"], newTypeName="InMemoryEvidenceLinkStore", filePath=…\InMemoryConversationRepository.cs)`. |
| Expected | Actionable refusal naming the referenced state. |
| Actual (verbatim) | `"Invalid operation: Refusing to extract type 'InMemoryEvidenceLinkStore' from 'InMemoryConversationRepository': the selected members reference state that would remain on the source type, so the generated code would not compile. Either include the referenced members in the extraction or perform a manual redesign first. Details: Extracted member may reference 'ConcurrentDictionary<Guid, List<MessageEvidenceLink>> InMemoryConversationRepository._evidenceLinks' which remains on the original type 'InMemoryConversationRepository' and is not available in the new type.; Extracted member may reference 'lambda expression' which remains on the original type 'InMemoryConversationRepository' and is not available in the new type.."` |
| Severity | **Not a bug** — PASS evidence for the refusal UX. Recorded so the next agent does not re-raise it. |
| UX follow-up (low priority) | `memberNames=["AddEvidenceLinksAsync", "_evidenceLinks"]` still produced the same refusal — the tool did not acknowledge the field in the input. Either accept field names in `memberNames`, or error with "fields are not a supported member kind; refactor the field into a property before retrying". |

### 9.5 `fix_all_preview` returns silent empty result for Info-severity diagnostics without a fix provider
| Field | Detail |
|-------|--------|
| Tool | `fix_all_preview` |
| Reproduction | `fix_all_preview(diagnosticId="IDE0130" | "IDE0290" | "IDE0008", scope="solution" | "project" | "document", …)` on a workspace where the relevant IDE/CA fix-provider assembly is not loaded. |
| Expected | Either (a) a non-null `guidanceMessage` telling the caller "no code fix provider is loaded" (as happens on Warning-severity IDs), or (b) explicit `matchedCount` + `fixableCount` fields so the caller can distinguish "no matches" from "matches exist but no provider loaded". |
| Actual | `{ previewToken:"", diagnosticId:"IDE0130", scope:"solution", fixedCount:0, changes:[], guidanceMessage:null }`. When the same tool is called with `CA1826` / `xUnit1051` instead, a helpful `guidanceMessage` IS produced. |
| Observed (verbatim, Info vs Warning side-by-side) | `IDE0130 → "guidanceMessage":null`; `CA1826 → "guidanceMessage":"No code fix provider is loaded for diagnostic 'CA1826'. Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs."` |
| Severity | **Medium** — confusing for agents: the silent path looks identical to "0 occurrences". |
| Suggested fix for next agent | Emit the same `guidanceMessage` template on all severities when the diagnostic has occurrences but no registered fix provider. Additionally, consider surfacing `occurrenceCount` in the preview response regardless of fixability. |

### 9.6 `bulk_replace_type` `scope=parameters` ignores generic arguments in implemented interfaces
| Field | Detail |
|-------|--------|
| Tool | `bulk_replace_type_preview` / `bulk_replace_type_apply` |
| Reproduction | 1. Extract `IDatabaseOptions` from `DatabaseOptions` (Phase 1c). 2. `bulk_replace_type_preview(oldTypeName="DatabaseOptions", newTypeName="IDatabaseOptions", scope="parameters")`. 3. Apply. 4. `compile_check`. |
| Expected | Either (a) rewrite both the method parameter *and* the `IValidateOptions<DatabaseOptions>` generic argument so the class still correctly implements the interface contract, or (b) surface a warning in the preview (`warnings[]` is currently `null`) that the method signature will diverge from the interface. |
| Actual | Only the parameter type is rewritten. The class still declares `IValidateOptions<DatabaseOptions>` but the `Validate` method now takes `IDatabaseOptions options`, which breaks the interface's exact-match method signature requirement. Workspace needed `git checkout` to recover. |
| Severity | **Medium** — the tool faithfully did what `scope=parameters` suggests, but the result is almost always wrong in practice when the parameter type appears in the type's implemented-interface generic signature. |
| Suggested fix for next agent | At minimum emit a `warnings[]` entry when `scope=parameters` touches a parameter whose declaring method overrides/implements a member whose base/interface signature uses the old type. Ideal fix: default scope for this case to `all` or refuse. |

### 9.7 `extract_method_preview` produces output that violates project formatting
| Field | Detail |
|-------|--------|
| Tool | `extract_method_preview` (and therefore `extract_method_apply` when the preview is applied) |
| Reproduction | `extract_method_preview(filePath=…\AmbiguityDetector.cs, startLine=42, startColumn=9, endLine=46, endColumn=23, methodName="NormalizeQuestionWords")`. |
| Expected | Output respects project `.editorconfig`: 4-space indent on the new method declaration, spaces around `=` at call site, preserved multi-line LINQ chain. |
| Actual | Preview diff shows: `var words=NormalizeQuestionWords(questionText);` (no spaces around `=`); `private List<string>? NormalizeQuestionWords(string? questionText)` declared at column 1 with no class-scope indent; multi-line chain `.Split(...).Select(...).Where(...).ToList()` collapsed into a single long physical line. |
| Observed (verbatim diff extract) | ``-        string normalized = questionText.Trim().ToLowerInvariant();`` → ``+        var words=NormalizeQuestionWords(questionText);``; and ``+private List<string>? NormalizeQuestionWords(string? questionText)`` with body on one line. |
| Severity | **Medium** — compiles, but requires a follow-up `format_document_apply` to become clean. The preview diff is also misleading because the insertion indent is wrong, which makes reviews harder. |
| Suggested fix for next agent | Run the generated method through the Roslyn `Formatter.Format` pass (same mechanism `format_document_preview` uses) before emitting the preview, and apply EndOfLine/indent rules from the nearest `.editorconfig`. Preserve original trivia for the extracted block (don't flatten multi-line method chains). |
| Bonus finding | The nullable return type `List<string>?` is pessimistic — at the extracted call site `questionText` is non-null (checked by the method's own early return at line 35), yet the extracted method accepts `string? questionText`. Not a defect on its own, but it shows the extractor does not consider the caller's null-state narrowing. |

### 9.8 `revert_last_apply` documented limitation interacts poorly with `extract_*_apply` (side-effect cleanup)
| Field | Detail |
|-------|--------|
| Tool | `revert_last_apply` (stable, Phase 5e) |
| Behaviour (per docs) | "Roslyn preview/apply operations … AND apply_text_edit / apply_multi_file_edit register for undo. File create/delete/move and project file mutations are not revertible." |
| Observed | After `extract_type_apply`, `revert_last_apply` correctly restored the edited source file but left: (1) the two new `RemoveConversationExpiresAtUtcRollback.cs` files, and (2) the `<Compile Include>` mutation in the csproj. The session had to fall back to `git checkout` + `rm` to recover. |
| Severity | **Documentation-vs-behaviour alignment** — the doc is accurate, but the practical effect is that `extract_type_apply` is effectively a non-revertible tool. |
| Suggested fix for next agent | Either (a) make `extract_*_apply` stop mutating the project file and writing duplicate files (Bugs 9.1 & 9.2), at which point `revert_last_apply` would be sufficient, or (b) expand `revert_last_apply`'s slot to include a file-manifest + csproj-snapshot for any tool that touches those axes. |

## 10. Improvement suggestions

- `workspace_load` — when `workspaceWarningCount` / `workspaceErrorCount` are >0 on the initial load and `restoreHint` is non-null, consider an optional `autoRestore=true` parameter that runs `dotnet restore` and reloads automatically. Current session pattern is: load → see CS0246 avalanche → realise restore wasn't run → manually restore → reload.
- `fix_all_preview` — return `occurrenceCount` alongside `fixedCount` so the caller can tell "0 occurrences" from "occurrences but no fix provider" (see §9.5).
- `extract_*_apply` — include the pre-change csproj snapshot in the preview token so `revert_last_apply` can unwind project-file edits. Related: §9.8.
- `extract_type_preview` — document whether `memberNames` accepts fields; either support or refuse with a clear message (§9.4 follow-up).
- `bulk_replace_type_preview` — when `scope=parameters` touches a parameter whose declaring method is an interface/base-class implementation, emit a `warnings[]` entry (see §9.6).
- All `*_preview` tools — ensure `changes[].unifiedDiff` enumerates every file that `*_apply` will modify (currently csproj changes are hidden; see §9.1, §4).
- `extract_method_preview` — run generated code through `Formatter.Format` + `.editorconfig` before returning (see §9.7).
- Cosmetic: on refusal messages, strip the trailing double-dot (`block scope..` should be `block scope.`). Seen on `extract_method_preview` error text.

## 11. Known issue regression check

> Phase 11 not executed. The prior audit file `20260413T180545Z_itchatbot_experimental-promotion.md` (against `1.12.0`) lists candidates but this run did not reproduce them systematically. Below are the items surfaced incidentally and the agent's best-effort status; treat as **unverified** until Phase 11 is re-run.

| Source id / area | Summary | Status (this run) |
|------------------|---------|--------------------|
| prior-audit `extract_type_apply` row | Prior run marked `extract_type_apply` as `exercised-apply` with 5677 ms. This run shows **new** defects: csproj mutation + duplicate-file write + preserved-override. | **regression (new findings)** — see §9.1, §9.2, §9.3 |
| prior-audit `extract_interface_apply` row | Prior run marked `extract_interface_apply` as `exercised-apply` with 5623 ms. This run shows **new** csproj-mutation defect. | **regression (new findings)** — see §9.1 |
| prior-audit `revert_last_apply` row | Prior run marked it as `exercised` with positive and negative probes. This run shows it does not cover `extract_type_apply` side-effects (documented behaviour, but a practical gap). | **no change** — see §9.8 |
| prior-audit `bulk_replace_type` row | Prior run used an "identity-replacement probe"; no issue reported. This run exposed the `scope=parameters` vs. implemented-interface gap. | **new finding** — see §9.6 |

---

## Hand-off notes for the next agent

- The worktree `worktree-experimental-promotion-2026-04-15` was cleaned via `git checkout` + manual removal of `src/config/IDatabaseOptions.cs` and exited via `ExitWorktree(action="remove")` at the end of this session. The branch no longer exists.
- To re-run Phase 1 reproductions: create a fresh disposable worktree, `workspace_load ITChatBot.sln`, `dotnet restore`, `workspace_reload`, then follow each §9 entry's "Reproduction" step.
- The highest-leverage next step is fixing §9.1 (csproj mutation) — it unlocks `extract_interface_apply` and `extract_type_apply` for real use and for the remainder of this exercise. §9.2 and §9.3 also live in `extract_type_apply` and are likely the same code path.
- The next run should execute Phases 2–11 in order. Phase 2 (file operations) should be straightforward; Phase 3 is partially `skipped-repo-shape` (no CPM); Phase 4 should exercise v1.17's `scaffold_test_batch_preview` token path; Phase 7c has four un-exercised v1.17/v1.18 additions that are the highest-value un-audited surface.
