# Backlog sweep plan — 2026-05-12T19:46:49Z

**Backlog snapshot:** `ai_docs/backlog.md` updated_at 2026-05-12T19:49:18Z (re-read post-deepener)
**Mode:** prepared via `/backlog-sweep:prepare`
**Selection summary:** 4 initiatives selected. Pre-split applied: `filepaths-array-vs-stringified-tool-description-clarification-batch-2` → 3 tool-surface-only children. After deepener, #1 is **obsolete** (already-clean target scope) → 3 actionable initiatives remain, all sequential.
**Anchor verification:** performed (deepeners verified anchors live in tree at plan-draft time).
**Skipped (with reason):**
- 7 Reserved rows (gh #606–612) — contributor pickup.
- 4 weaker-evidence rows (`compile-check-not-connected-...`, `list-analyzers-totalrules-variance`, `scaffolding-service-split-by-scaffold-type`, `tool-surface-pagination-or-tool-sets`).
- 4 Defer section rows.
- `skill-namespace-installed-as-bulk-frontmatter-migration` — maintainer choice (dedicated session, not a sweep).

**Conflict graph summary:** edges (2,3), (3,4), (2,4) — all share `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`'s `inScopePairs` HashSet. Triangle ⇒ strict serial order 2 → 3 → 4. #1 is zero-degree but obsolete.

**Scheduling note:** no initiative touches an addenda-listed hotspot (`WorkspaceManager.cs`, `ServerSurfaceCatalog.cs`, `ServiceCollectionExtensions.cs`); no parallel-wave hotspot gating concerns.

---

### 1. skill-prompts-deprecated-workspace-load-param-name-cleanup

| Field | Content |
|---|---|
| Status | obsolete (live grep confirms 0 occurrences of `solutionOrProjectPath` in `.claude/skills/**/*.md`, `skills/**/*.md`, or `ai_docs/prompts/**/*.md` — all three target scopes already clean) |
| Backlog rows closed | `skill-prompts-deprecated-workspace-load-param-name-cleanup` |
| Diagnosis | Grep across all three mandated scopes returns 0 hits for `solutionOrProjectPath`. The three file matches that exist in the repo are: (1) `ai_docs/backlog.md` — the backlog row text itself, which quotes the deprecated name as the target of the fix; (2) `review-inbox/archive/20260512T134225Z/20260512T130723Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — historical session evidence; (3) nowhere in `.claude/skills/`, `skills/`, or `ai_docs/prompts/` — confirmed by three independent zero-result Grep calls at deepener invocation time. All `workspace_load` references in the shipped and local skill files use correct prose ("solution/project path") without citing the old `solutionOrProjectPath` parameter name literally as a tool argument. The archived sweep plan `20260512T135106Z` initiative 18 reached the same conclusion. This is a docs-hygiene gap that has already self-healed; no actionable target remains. The regression test proposed in the backlog row (asserting zero occurrences in `eng/verify-skills-are-generic.ps1`) is also moot: `verify-skills-are-generic.ps1` checks shipped `skills/` for repo-coupling patterns — it is not the appropriate vehicle for deprecated-parameter-name detection, and the symptom it would have guarded against is already absent. |
| Approach | None required. Executor: close the backlog row immediately without opening a worktree or branch (Step 5a obsolete path). |
| Scope | 0 production files, 0 test files. No changes required. |
| Tool policy | edit-only |
| Estimated context cost | 5000 |
| Risks | No risks — no changes made. The backlog row text and retro archive legitimately retain the deprecated name as historical evidence; those locations are not actionable targets per the row's own scope definition. |
| Validation | Re-run `Grep` for `solutionOrProjectPath` scoped to `.claude/skills/**/*.md`, `skills/**/*.md`, and `ai_docs/prompts/**/*.md` — expect 0 hits. |
| Performance review | N/A |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Confirmed `workspace_load` parameter canonicalization already complete across all skill prompts and `ai_docs/prompts/` files; backlog row `skill-prompts-deprecated-workspace-load-param-name-cleanup` closed as obsolete (0 occurrences in all target scopes). |
| Backlog sync | Close rows: [`skill-prompts-deprecated-workspace-load-param-name-cleanup`]. Mark obsolete. |

### 2. filepaths-batch-2a-advanced-msbuild

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | filepaths-batch-2a-advanced-msbuild |
| Diagnosis | Both parameters are confirmed missing the "native JSON array" guard phrase. `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:150` — parameter `filePaths` (`IReadOnlyList<string>?`) on tool `get_complexity_metrics` carries `[Description("Optional: list of source file paths to include (union with filePath)...")]` with no "native JSON array" mention. `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs:60` — parameter `includedNames` (`string[]?`) on tool `get_msbuild_properties` carries `[Description("Optional: explicit allowlist of property names to return. Takes precedence over propertyNameFilter when supplied.")]` with no "native JSON array" mention. The lockstep test at `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs:264` (`AllArrayTypedToolParameters_DescriptionContainsNativeJsonArrayPhrase`) currently enumerates 4 pairs (added by PR #697); these 2 pairs are absent from the `inScopePairs` HashSet, so the test would not catch regressions on them today. |
| Approach | 1. In `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:150`, extend the `filePaths` `[Description]` to include the "native JSON array" guard phrase + concrete example, mirroring the pattern from PR #697. 2. In `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs:60`, extend the `includedNames` `[Description]` similarly. 3. In `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`, extend the `inScopePairs` HashSet inside `AllArrayTypedToolParameters_DescriptionContainsNativeJsonArrayPhrase` (currently ~line 272) with `("get_complexity_metrics", "filePaths")` and `("get_msbuild_properties", "includedNames")`. Update the comment to reflect the expanded count. No new test methods required. |
| Scope | Production files: 2 — `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs`. Test files modified: 1 — `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`. Rule 3 exemption: tool-surface-only, 2 production files (addenda § Tool-surface-only exemption). |
| Tool policy | edit-only |
| Estimated context cost | 22000 |
| Risks | (1) The `inScopePairs` HashSet is a positive allowlist; adding the 2 new pairs increases `foundCount` from 4 to 6. Guard `foundCount > 0` unaffected. (2) Tool names `get_complexity_metrics` / `get_msbuild_properties` must not have been renamed between plan-draft and execution (confirmed live at draft time). (3) Sibling batch-2 initiatives (#3, #4) also extend `SurfaceCatalogTests.cs` — serial execution mandatory; do not parallel-wave. |
| Validation | 1. `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`. 2. `mcp__roslyn__test_run --filter "AllArrayTypedToolParameters_DescriptionContainsNativeJsonArrayPhrase"` — passes with `foundCount == 6` (or higher if siblings already ran). 3. Verify both edited `[Description]` strings contain literal `native JSON array` (case-sensitive, `StringComparison.Ordinal`). 4. `./eng/verify-ai-docs.ps1`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `get_complexity_metrics` (`filePaths`) and `get_msbuild_properties` (`includedNames`) parameter descriptions to include the "native JSON array" guard phrase, preventing LLM clients from mis-encoding array arguments as stringified JSON. |
| Backlog sync | Close rows: [filepaths-batch-2a-advanced-msbuild]. |

### 3. filepaths-batch-2b-interface-scaffolding

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | filepaths-batch-2b-interface-scaffolding |
| Diagnosis | Both anchors confirmed in current tree. `InterfaceExtractionTools.cs:30` declares `[Description("Optional: specific member names to include. If omitted, all public instance members are included.")] string[]? memberNames = null` — no "native JSON array" guard phrase. `ScaffoldingTools.cs:35` declares `[Description("Optional: additional interface names to declare on the scaffolded type")] string[]? interfaces = null` — same omission. The existing lockstep test holds 4 pairs from batch-2 wave 1; extending with 2 new pairs enforces the guard on these parameters. |
| Approach | 1. `src/RoslynMcp.Host.Stdio/Tools/InterfaceExtractionTools.cs:30` — rewrite `[Description]` on `memberNames` to include the "native JSON array" guard phrase + example (mirror `changedFilePaths`/`projects` pattern from batch-2 wave 1). 2. `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:35` — apply same treatment to `interfaces`. 3. `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs` — add `("extract_interface_preview", "memberNames")` and `("scaffold_type_preview", "interfaces")` to `inScopePairs`. |
| Scope | Production files: 2 — `src/RoslynMcp.Host.Stdio/Tools/InterfaceExtractionTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`. Test files modified: 1 — `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`. Rule 3 exemption: tool-surface-only, 2 files. |
| Tool policy | edit-only |
| Estimated context cost | 22000 |
| Risks | (1) Guard phrase wording must match the test's case-sensitive `StringComparison.Ordinal` check on literal `"native JSON array"`. (2) Tool names must match `McpServerToolAttribute.Name` verbatim: `extract_interface_preview`, `scaffold_type_preview`. (3) Must execute serially after #2 — shared HashSet in `SurfaceCatalogTests.cs`. (4) `memberNames` is on `PreviewExtractInterface` only; `ApplyExtractInterface` has no array params. (5) Verify no new `string[]` params appeared in `ScaffoldingTools.cs` between draft and execution (`scaffold_test_batch_preview`'s `targets` is `ScaffoldTestBatchTargetDto[]`, out of scope). |
| Validation | 1. `mcp__roslyn__compile_check` after each edit. 2. Targeted test passes with `foundCount` ≥ 6. 3. Full `SurfaceCatalogTests` class green. 4. Verify guard phrase appears literally in both edited descriptions. |
| Performance review | N/A |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed missing "native JSON array" guard phrase in `[Description]` attributes on `extract_interface_preview`'s `memberNames` parameter and `scaffold_type_preview`'s `interfaces` parameter. |
| Backlog sync | Close rows: [filepaths-batch-2b-interface-scaffolding]. |

### 4. filepaths-batch-2c-parameter-object

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `filepaths-batch-2c-parameter-object` |
| Diagnosis | `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs:37` confirms the anchor is live: `string[]? dtoFolders = null` with `[Description("Optional folder segments under the project root for the new record file. Defaults to folders derived from the namespace.")]` — no "native JSON array" guard. The lockstep test at `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs:271-277` holds 4 pairs; `parameter_object_preview/dtoFolders` is absent, so no current coverage for this parameter. |
| Approach | **1 production edit** — `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs:37`: rewrite `[Description]` on `dtoFolders` to include the "native JSON array" guard phrase + example. **1 test extension** — `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`: add `("parameter_object_preview", "dtoFolders")` to `inScopePairs`, update the comment to reference 5 pairs (was 4). |
| Scope | Production files: 1 — `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs`. Test files modified: 1 — `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`. Rule 3 exemption: tool-surface-only. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) Sequencing constraint (after #3) is load-bearing: shared test file. (2) Fanout probe skipped — surgical `[Description]` edit on a single parameter. (3) If `dtoFolders` is ever renamed/removed, the new entry becomes silent no-op; `foundCount` guard would still surface this. |
| Validation | 1. `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`. 2. Targeted test passes with `foundCount == 5` (or higher if earlier siblings already ran). 3. Sibling `ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts` + `McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` remain green. |
| Performance review | N/A |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `parameter_object_preview` tool's `dtoFolders` parameter description to include the "native JSON array" guard phrase and example, matching the clarification pattern applied to other array-typed parameters in PR #697. |
| Backlog sync | Close rows: [`filepaths-batch-2c-parameter-object`]. |
