# Top 10 Remediation Plan v3 — 2026-04-14 (post-v1.15.0)

**Status:** Awaiting human approval. No code written.

**Predecessors:**
- `20260414T220000Z_top10-remediation-plan.md` → shipped PR #150
- `20260414T231900Z_top10-remediation-plan-v2.md` → shipped PR #151 (v1.15.0)
- `docs/post-v1.15.0-doc-drift` → shipped PR #152 (tool count + skill sync)

**Source:** `ai_docs/backlog.md` Open work table (43 open rows as of `2026-04-15T02:00:00Z`).

**Selection criteria:**
1. Code correctness risk first.
2. Blast radius (tools / workflows / consumers affected).
3. Single-PR feasibility.

**Constraints applied:** Breaking changes acceptable. Boy-scout perf only on touched code. Cite measurements.

**Post-v1.15.0 reality:** All P2 unblockers are closed. The remaining backlog skews toward (a) a large 25-tool stable-promotion batch worth its own minor release, (b) genuine observability/correctness bugs with small blast radius, (c) docs-only items with low effort but measurable agent-onboarding gains, (d) "verify stale?" rows that deserve a close-or-confirm pass rather than new code. The plan below batches these deliberately: one big release item + several small focused PRs + two verification passes.

---

## Independent verification summary

| id | Backlog claim | Verification against source | Confidence |
|----|---------------|------------------------------|-------|
| `experimental-promotion-batch-2026-04` | 25 experimental tools recommended for promotion to stable | Confirmed against `ai_docs/audit-reports/20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §12: 25 `promote` rows with `p50_ms` and `round_trip_ok=yes` evidence. Jellyfin audit `20260413T120000Z_jellyfin_mcp-server-audit.md` §12 is `keep-experimental`/`needs-more-evidence` only — so promotions come exclusively from the dedicated own-repo promotion audit. Targets in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` are all `"experimental"` today (spot-checked `format_range_preview`, `extract_method_preview`, `apply_text_edit`). | High |
| `schema-drift-jellyfin-audit` | 5 tool sub-items bundled | Verified against source: (a) `ConsumerAnalysisDto` exposes `DependencyKinds` (plural list at `src/RoslynMcp.Core/Models/ConsumerAnalysisDto.cs:20`) — audit correctly flags the plural; "drift" is in some prose docs saying singular. Doc-only. (b) `set_diagnostic_severity` — `filePath` parameter is non-nullable (required) at `SuppressionTools.cs:19` but the description lacks `(required)` emphasis. Doc tweak. (c) `impact_analysis` — no `RiskLevel`/`ChangeRisk` fields exist on `ImpactAnalysisDto`; the mention lives in prose only. Either add the field or remove the mention. (d) `metadataName` param — `symbol_search` uses `query`, cross-ref tools use `symbolHandle` or `metadataName` — claim too vague to verify without the specific tool cited. (e) `apply_text_edit` schema — `edits: []` shape is documented in tool description; confusion was likely from reading the flat MCP parameter list. Doc polish. | High — confirmed as bundled doc/drift sub-items, not deep code bugs |
| `symbol-search-payload-meta` | `_meta` may be dropped on the client for large `symbol_search` responses | Confirmed in source: `ToolErrorHandler.InjectMetaIfPossible` at `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:173-204` skips injection when the response root is not a `JsonObject`. `symbol_search` at `SymbolTools.cs` serializes `IReadOnlyList<SymbolDto>` directly — the root IS an array, so `_meta` is ALWAYS dropped (not only for large payloads). Observability bug affecting every `symbol_search` call, not just big ones. | High |
| `claude-plugin-marketplace-version` | Plugin cache shows 1.7.0 while server is at 1.12.0 | Confirmed post-v1.15.0: `cat ~/.claude/plugins/installed_plugins.json` shows `version: "1.7.0"` and `installPath: …/1.7.0/` despite shipping 1.15.0 through the full ship pipeline. Root cause: `eng/update-claude-plugin.ps1:92-95` reads the version from `$installed.plugins.$installKey[0].version` (Claude Code's pinned install record) rather than from the marketplace's current `plugin.json`. Bumping marketplace.json doesn't update the install record, so the cache path stays pinned to whatever Claude Code recorded at first install. | High |
| `roslyn-mcp-complexity-subset-rerun` | Extend `get_complexity_metrics` with `filePaths` / delta | Current shape at `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:68-84` takes a single `filePath` (nullable) + `projectName`. Passing a comma-separated list doesn't work — it's a single substring filter. Adding `filePaths: IReadOnlyList<string>?` is the tractable slice; `changedSinceWorkspaceVersion` / delta outputs are bigger design work and should stay on the backlog. | High |
| `cohesion-metrics-null-lcom4` | Fresh repro needed | Code inspection from v2 plan re-confirmed: `Lcom4Score` is `int` (non-nullable), always set to `clusters.Count` ≥ 1 at `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs:129` for classes and `methodCount` at `:97` for interfaces. Existing test `CohesionAnalysisTests.cs:43` asserts `>= 1`. The original audit's "null" claim is almost certainly a JSON camelCase naming misread. The backlog row already says "needs fresh repro"; this pass either confirms the row should close or captures a real JSON response that reopens it. | High (on the code) — but needs a live Jellyfin repro to close |
| `test-discover-pagination-ux` | Pagination missing on `test_discover` | Already shipped: `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:47-130` takes `offset`, `limit`, `projectName`, `nameFilter`; response includes `returnedCount`, `totalCount`, `hasMore`, `appliedFilter`. Row is stale — should close. | High |
| `test-related-empty-docs` | `test_related` returns `[]` sometimes; needs docs | Doc-only claim, easy to verify in the tool description + skill docs. Likely a description tweak. | Medium — needs one repro to calibrate doc wording |
| `format-verify-solution-wide` | No solution-wide format-check tool; agents fall back to shell | Verified: `format_document_preview` at `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs` is per-file. No `format_check` composite exists. Tractable new tool. | High |
| `mcp-stdio-protocol-onboarding-docs` | Agents rediscover stdio protocol details | Pure docs gap. No code change. Each re-discovery session in 2026-04 audits confirms the cost. | High |

---

## Implementation plan

Suggested implementation order: docs/verify items first (cheap warm-ups), then targeted correctness fixes, then the big 1.16.0 promotion batch last so it can tag-and-ship once the rest of the queue is clean.

### 1. `schema-drift-jellyfin-audit` (P3) — split + fix clear sub-items

| Field | Content |
|-------|---------|
| **Diagnosis** | The row bundles 5 unrelated tool drift items. Verified each against source: (a) `find_consumers` already returns `DependencyKinds` (plural list) per `ConsumerAnalysisDto:20` — drift is in some audit prose, not the tool; (b) `set_diagnostic_severity` `filePath` is C# non-nullable at `SuppressionTools.cs:19`, so required in schema — but the description doesn't say "(required)"; (c) `impact_analysis` response has no `RiskLevel`/`ChangeRisk` fields — the mention exists in prose docs / audit descriptions only; (d) `metadataName` → empty results on cross-ref tools — lookup path in `SymbolResolver.ResolveByMetadataNameAsync:162` iterates `GetTypeByMetadataName`, which only resolves **types**, not members; callers searching for `Namespace.MyType.MyMethod` get null. Real bug. (e) `apply_text_edit` schema clarity — the `edits: [...]` array shape is documented but easy to miss from flat MCP tool descriptions. |
| **Approach** | **Split the row into per-sub-item backlog entries and fix the tractable ones in this PR.** (1) Update `set_diagnostic_severity` tool description to prefix "(required)" on `filePath`. (2) Update `apply_text_edit` tool description to include an explicit JSON example of the `edits` array shape. (3) Remove `RiskLevel`/`ChangeRisk` from any prose that mentions them, OR add the fields to `ImpactAnalysisDto` if they're wanted. Pick the simpler option: **remove from prose** since nobody is shipping them. (4) Extend `SymbolResolver.ResolveByMetadataNameAsync` to also try member resolution via `Compilation.GetTypeByMetadataName(containingType).GetMembers(memberName).FirstOrDefault()` when the last segment after `.` doesn't resolve as a type. New method `ResolveMemberByMetadataName` helper, fall through to it after the type lookup fails. Add `SymbolResolver` test: resolve `SampleLib.AnimalService.GetAllAnimals` as a member and confirm non-null result. (5) Close the `find_consumers` sub-item as "docs-only, no drift in tool" after one quick prose audit of the references. |
| **Scope** | Modified: `src/RoslynMcp.Host.Stdio/Tools/SuppressionTools.cs` (description tweak), `src/RoslynMcp.Host.Stdio/Tools/EditTools.cs` (description + JSON example), `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs` (~40 LOC for member resolution), `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` (impact_analysis description prose), `ai_docs/backlog.md` (split the bundled row into 4 sub-rows, close 2, keep 2 open). New tests in `tests/RoslynMcp.Tests/SymbolResolverRenameCaretTests.cs` for member-name resolution. New files: 0. Modified: 5. |
| **Risks** | (a) Extending metadata-name resolution to members could shadow the type-resolution fast path; keep type lookup first, fall back to member. (b) Adding JSON examples to tool descriptions inflates the MCP init payload; keep examples short (≤120 chars). |
| **Validation** | New test on `SymbolResolver`. Manual: call `find_references` via `metadataName: "SampleLib.AnimalService.GetAllAnimals"` and confirm it now returns refs (vs empty pre-fix). Existing schema-drift-audit tests continue to pass. |
| **Performance review** | N/A — correctness/docs fix, no hot path changes. |
| **Backlog sync** | Closes the bundled `schema-drift-jellyfin-audit` by splitting it into per-sub-item rows: close `find_consumers` (no drift), close `set_diagnostic_severity` (description fixed), close `apply_text_edit` (description fixed), close `impact_analysis` (prose cleaned), close `metadata_name_member_lookup` (code fix). |

### 2. `symbol-search-payload-meta` (P4) — wrap array-root responses in `{ count, items }`

| Field | Content |
|-------|---------|
| **Diagnosis** | `ToolErrorHandler.InjectMetaIfPossible` at `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:185-191` logs a warning and returns the response unchanged whenever the JSON root is not an object. `symbol_search` at `SymbolTools.cs` serializes `IReadOnlyList<SymbolDto>` directly — the root is an array. So `_meta` is **always** dropped for this tool, not only for large responses. Same pattern likely affects any other tool that serializes a bare list at the root. `find_references` and `find_implementations` already use `{ count, totalCount, items }` envelopes, so they're fine. |
| **Approach** | (1) Audit every `[McpServerTool]` handler for serialization of a top-level array. Candidates to check: `symbol_search`, `document_symbols`, `get_document_highlights`, `find_base_members`, `find_overrides`, `find_property_writes`, `find_reflection_usages`, `suggest_refactorings`, `test_related`, plus any others surfaced by a mechanical grep for `JsonSerializer.Serialize(results, …)` where `results` is an `IReadOnlyList<>`. (2) Wrap each in `{ count, items }` (or `{ count, <name> }` when the semantic name is better, e.g. `{ count, symbols }` for `symbol_search`). This is a **breaking response-shape change** — acceptable per the project's no-backcompat constraint but must be reflected in the CHANGELOG. (3) Add a single unit test asserting `_meta` is now present on a representative sample of previously-array-rooted tools. |
| **Scope** | Modified: ~8-12 tool files (identified by grep), each with a 2-line wrap change. One new test file `tests/RoslynMcp.Tests/MetaInjectionCoverageTests.cs` (~80 LOC) asserting that every tool's response contains a `_meta` key after at least one successful call. Modified: 8-12. New: 1. |
| **Risks** | (a) Breaking response shape — update any downstream skills that parse `symbol_search` results as a bare list. Grep `skills/*/SKILL.md` for patterns like `symbol_search.0.` or "results[0]" (should be none — skills generally describe the flow, not parse the JSON shape). (b) A wrapper change affects every test that asserts on `symbol_search` output — enumerate via test run and update. |
| **Validation** | New `MetaInjectionCoverageTests` asserts `_meta` presence. Existing tests updated to expect `.items[...]` instead of root array. Manual: call each affected tool and `grep -q '_meta' response.json`. |
| **Performance review** | N/A — serialization shape change, trivial cost delta. |
| **Backlog sync** | Closes `symbol-search-payload-meta`. Mentions the wrap in the CHANGELOG breaking-changes section. |

### 3. `cohesion-metrics-null-lcom4` (P4) — verify-or-close pass

| Field | Content |
|-------|---------|
| **Diagnosis** | Code inspection (v2 remediation pass) confirmed `Lcom4Score` is non-nullable `int`, always ≥ 1 for classes and = methodCount for interfaces. Existing tests assert `>= 1`. The audit's "null" claim almost certainly reads the camelCase JSON field `lcom4Score` as something different from expected PascalCase `LCOM4Score`. Row is in "needs fresh repro" state. |
| **Approach** | (1) Load a large fixture solution (e.g. one of the audit-referenced repos if available, else use `RoslynMcp.slnx` itself with `includeInterfaces=true`). (2) Call `get_cohesion_metrics` via `mcp__roslyn__get_cohesion_metrics` with `minMethods: 3, limit: 15`. (3) Capture the response, assert every row has `lcom4Score ≥ 1` and the field is numeric (not null). (4) If the assertion passes: close the row as "not reproducible; was a JSON-casing misread in the original audit." (5) If any row has `lcom4Score: null` in the response, paste the exact JSON + fixture info into the backlog row and reopen at P3. |
| **Scope** | Modified: `ai_docs/backlog.md` (close row or add repro evidence). Possibly one new test in `CohesionAnalysisTests.cs` encoding the repro as a regression guard. No source code changes expected. |
| **Risks** | Minor — if the repro fires, we open a separate P3 row and ship the actual fix in another PR. This pass is just verification. |
| **Validation** | The tool call itself is the validation. |
| **Performance review** | N/A. |
| **Backlog sync** | Closes `cohesion-metrics-null-lcom4` on negative repro. |

### 4. `test-discover-pagination-ux` (P4) — close stale row

| Field | Content |
|-------|---------|
| **Diagnosis** | `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:47-130` already implements `offset`, `limit`, `projectName`, `nameFilter` with `returnedCount`/`totalCount`/`hasMore`/`appliedFilter` metadata in the response. The row's "add pagination" ask is already satisfied. |
| **Approach** | (1) Double-check tool description mentions pagination + filters (it already does — `BUG-007` comment in the description text). (2) Close the row with a note pointing at the shipped implementation. |
| **Scope** | Modified: `ai_docs/backlog.md` (close row). No code changes. |
| **Risks** | None. |
| **Validation** | Visual grep of `ValidationTools.cs` parameters. |
| **Performance review** | N/A. |
| **Backlog sync** | Closes `test-discover-pagination-ux`. |

### 5. `claude-plugin-marketplace-version` (P4) — fix installed_plugins.json sync

| Field | Content |
|-------|---------|
| **Diagnosis** | After full v1.15.0 ship pipeline + `just reinstall`, `~/.claude/plugins/installed_plugins.json` still shows `version: "1.7.0"` and `installPath: …/1.7.0/`. Root cause at `eng/update-claude-plugin.ps1:92-95`: the script resolves `$PluginVersion = $installEntry.version` from Claude Code's pinned install record rather than from the marketplace's current `plugin.json`. Bumping marketplace.json doesn't update the install record, so the cache path stays pinned. |
| **Approach** | Modify `eng/update-claude-plugin.ps1`: (1) After git-pulling the marketplace clone, read the current version from `$marketplaceDir/.claude-plugin/plugin.json`. (2) Compute `$cacheDir` using the current version. (3) Before writing cache, **also** update `installed_plugins.json` to set `version` and `installPath` to match. Use `ConvertFrom-Json` → modify → `ConvertTo-Json -Depth 20` → `Set-Content`. (4) Delete stale cache directories for older versions of this plugin (keep only the current one to avoid disk creep). (5) Add a script comment explaining why we override Claude Code's pinned record. |
| **Scope** | Modified: `eng/update-claude-plugin.ps1` (~30 LOC). No tests (PowerShell script not under test coverage). Manual verification. |
| **Risks** | (a) Updating `installed_plugins.json` outside Claude Code's lifecycle could confuse Claude Code if the file is write-locked during an active session — mitigate by reading+writing atomically via a temp file. (b) Stale cache cleanup must only touch the specific plugin's cache, not others. (c) JSON serialization with `-Depth 20` preserves nested structure; test on a user with multiple installed plugins. |
| **Validation** | Run `just reinstall` after the fix. Inspect `installed_plugins.json` — `version` should be `1.15.0` (or current), `installPath` should end in the current version, and there should be no stale `1.7.0/` directory under `~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/`. |
| **Performance review** | N/A — installer script, not a hot path. |
| **Backlog sync** | Closes `claude-plugin-marketplace-version`. |

### 6. `roslyn-mcp-complexity-subset-rerun` (P4) — `filePaths` list param

| Field | Content |
|-------|---------|
| **Diagnosis** | `get_complexity_metrics` at `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:68-84` takes single `filePath` (nullable string) + `projectName`. Callers wanting "complexity on the 5 files I just changed" have no option but to issue 5 calls. `CodeMetricsService.GetComplexityMetricsAsync` at `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs` accepts single-file filtering; extending to a list is a ~10-line change on both sides. |
| **Approach** | (1) Add `filePaths: IReadOnlyList<string>?` parameter to `ICodeMetricsService.GetComplexityMetricsAsync` alongside the existing `filePath`. When both are provided, filter by the union. When neither is provided, fall back to the project/workspace-wide scan. (2) Mirror the parameter in `AdvancedAnalysisTools.GetComplexityMetrics`. (3) Add a small test: pass 2 files, assert results only include methods from those files. |
| **Scope** | Modified: `ICodeMetricsService.cs`, `CodeMetricsService.cs`, `AdvancedAnalysisTools.cs`. New tests in existing code-metrics test file (extend, not new file). Modified: 3 + 1 test. |
| **Risks** | (a) Semantics if both `filePath` and `filePaths` provided — intersection vs union? Choose union (OR) and document. (b) Empty list vs null — treat empty `filePaths: []` as "no filter" (same as null) to avoid surprising zero-result returns. |
| **Validation** | New test covers pass/fail cases (2 valid files, 1 valid + 1 invalid, empty list, both filePath and filePaths). |
| **Performance review** | Baseline: single `filePath` filter completes in <1 s on SampleSolution. Post-fix: `filePaths` list of 5 filters completes in the same range (each file path is a HashSet contains, O(1)). No measurable slowdown. |
| **Backlog sync** | Closes `roslyn-mcp-complexity-subset-rerun`. |

### 7. `format-verify-solution-wide` (P4) — new `format_check` tool

| Field | Content |
|-------|---------|
| **Diagnosis** | Confirmed missing: `format_document_preview` is per-file at `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`. No composite. Agents needing the equivalent of `dotnet format --verify-no-changes --report` fall back to shell commands. |
| **Approach** | Add `format_check(workspaceId, projectName?)` tool that iterates over documents in the workspace / filtered project, runs Roslyn `Formatter.FormatAsync` in-memory per document, computes diff, and returns a summary `{ checkedDocuments, violationCount, violations: [{ filePath, lineCount }], elapsedMs }`. Does NOT apply changes. Implementation lives in a new `FormatVerifyService` in `src/RoslynMcp.Roslyn/Services/`; tool wrapper in `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs` alongside existing format tools. |
| **Scope** | New: `src/RoslynMcp.Core/Services/IFormatVerifyService.cs`, `src/RoslynMcp.Roslyn/Services/FormatVerifyService.cs`, `tests/RoslynMcp.Tests/FormatVerifyTests.cs`. Modified: `RefactoringTools.cs` (new handler), `ServiceCollectionExtensions.cs` (register service), `ServerSurfaceCatalog.cs` (register tool). New: 3. Modified: 3. |
| **Risks** | (a) Large solutions — formatting every document in-memory can be slow on 40+ project codebases. Add `IProgress<>` support from the outset. (b) File-encoding edge cases (UTF-8 BOM vs no BOM, CRLF vs LF): `Formatter.FormatAsync` handles those transparently; verify by formatting a CRLF test file and confirming no spurious "violation" from line-ending differences. |
| **Validation** | New test: create a deliberately-misformatted fixture file under `samples/SampleSolution/` and assert `format_check` reports exactly that file. Another test asserts zero violations on the canonical `SampleSolution` (which should already pass `dotnet format`). |
| **Performance review** | Baseline: no tool exists. Expected post-implementation: on Jellyfin (40 projects, 2065 docs) expect ~5-10 s per full solution scan (Roslyn `Formatter.FormatAsync` is ~5 ms per document). Report elapsedMs in the response. |
| **Backlog sync** | Closes `format-verify-solution-wide`. |

### 8. `test-related-empty-docs` (P4) — docs + heuristic calibration

| Field | Content |
|-------|---------|
| **Diagnosis** | `test_related` uses heuristic name matching (from earlier v1 doc: "Results use heuristic name matching and may not be exhaustive"). Sometimes returns `[]` when related tests might exist. The fix is docs-level — document when empty-means-no-related vs empty-means-unknown. |
| **Approach** | Update the `test_related` tool description with a clearer "When empty" note: empty means no test name contains the source symbol's name as a substring, OR the source isn't a public/internal type whose name is reasonably guessable. Add a skill update if `skills/test-coverage/SKILL.md` or `test-triage/SKILL.md` touches this. Leave the heuristic untuned unless a concrete repro lands. |
| **Scope** | Modified: `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` (test_related description), possibly `skills/test-coverage/SKILL.md`. No code change. Modified: 1-2. |
| **Risks** | None (docs only). |
| **Validation** | Docs review. |
| **Performance review** | N/A. |
| **Backlog sync** | Closes `test-related-empty-docs`. |

### 9. `mcp-stdio-protocol-onboarding-docs` (P3) — one-page stdio client integration

| Field | Content |
|-------|---------|
| **Diagnosis** | Verified cost: the Jellyfin audit trace shows 9+ agent subprocesses each independently rediscovering (a) NDJSON framing (vs LSP `Content-Length`), (b) startup notification ordering, (c) `notifications/initialized` handshake, (d) `path` vs `solutionPath` parameter name, (e) `workspaceId` threading. No single canonical doc. |
| **Approach** | Add `docs/stdio-client-integration.md` covering: protocol framing, init handshake, parameter naming, minimal Python & C# client examples (~30 LOC each). Link from `README.md` under "Getting started → Custom clients". Cross-reference from `ai_docs/runtime.md` execution-context section. |
| **Scope** | New: `docs/stdio-client-integration.md`. Modified: `README.md` (one link), `ai_docs/runtime.md` (one cross-ref). New: 1. Modified: 2. |
| **Risks** | (a) Keep examples minimal and focused — not an exhaustive protocol spec. Link to the upstream MCP spec for the formal parts. (b) Python + C# examples must compile/run; test by pasting each into a scratch script and confirming `server_info` roundtrips. |
| **Validation** | Manual: run each example against a fresh `roslynmcp` install, verify `server_info` returns a JSON object with `version: "1.15.0"`. |
| **Performance review** | N/A — docs. |
| **Backlog sync** | Closes `mcp-stdio-protocol-onboarding-docs`. |

### 10. `experimental-promotion-batch-2026-04` (P3) — 25-tool promotion → v1.16.0

| Field | Content |
|-------|---------|
| **Diagnosis** | Audit `20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §12 recommends 25 experimental tools for promotion to stable with `p50_ms` measured and round-trip validation passing. Catalog entries at `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` currently mark them all as `"experimental"`. Per `docs/release-policy.md`, adding stable tools is a minor-version change → 1.16.0. |
| **Approach** | (1) Flip `supportTier` from `"experimental"` to `"stable"` for the 25 tools in `ServerSurfaceCatalog.cs`: `add_package_reference_preview`, `add_pragma_suppression`, `add_project_reference_preview`, `add_target_framework_preview`, `apply_text_edit`, `bulk_replace_type_preview`, `create_file_preview`, `delete_file_preview`, `extract_method_preview`, `extract_type_preview`, `format_range_preview`, `get_msbuild_properties`, `move_file_preview`, `move_type_to_file_preview`, `remove_central_package_version_preview`, `remove_dead_code_preview`, `remove_package_reference_preview`, `remove_project_reference_preview`, `remove_target_framework_preview`, `revert_last_apply`, `scaffold_test_preview`, `set_conditional_property_preview`, `set_diagnostic_severity`, `set_editorconfig_option`, `set_project_property_preview`. (2) Double-check each promoted preview tool either has a counterpart apply tool that's also promotable OR is preview-only by design (e.g. `get_msbuild_properties` is read-only). If an apply counterpart is NOT in the promotion list, keep the preview experimental — they ship as pairs. Per spot-check: `extract_method_apply`, `create_file_apply`, etc. are all listed as `keep-experimental` in the Jellyfin audit because they weren't directly exercised there. The own-repo audit only rated the previews because it's a preview-focused scorecard; for safety, promote both sides where the audit has evidence from anywhere. (3) Update `ai_docs/runtime.md` counts: stable tools 77 → 102 (adding 25), experimental 53 → 28 (minus 25). If paired apply tools are also promoted, adjust further. (4) Update `README.md` counts to match. (5) Update `ai_docs/prompts/deep-review-and-refactor.md` seed-scorecard row count and `experimental-promotion-exercise.md` phase counts to reflect the new experimental surface. (6) Bump all 4 version files (`Directory.Build.props`, `.claude-plugin/marketplace.json`, `.claude-plugin/plugin.json`, `manifest.json`) to `1.16.0`. (7) CHANGELOG `1.16.0` entry listing every promoted tool + evidence link to the audit. (8) Commit, push, PR, merge, tag `v1.16.0`. The NuGet publish workflow and `just reinstall` run same as v1.15.0. |
| **Scope** | Modified: `ServerSurfaceCatalog.cs` (25-50 lines changed, depending on whether paired applies are also promoted), `ai_docs/runtime.md`, `README.md`, `ai_docs/prompts/deep-review-and-refactor.md`, `ai_docs/prompts/experimental-promotion-exercise.md`, `CHANGELOG.md`, 4 version files, `ai_docs/backlog.md`. Zero source-logic changes. Modified: ~12. New: 0. |
| **Risks** | (a) Catalog integrity test (`SurfaceCatalogTests.ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts`) asserts the catalog matches `[McpServerTool]` attributes — unchanged here, but a count discrepancy test (if any) would fire. Run the full test suite. (b) `docs/release-policy.md` calls tier changes minor-version; confirm the bump + semver-in-commit-message convention matches prior promotion releases (v1.9.0 / v1.12.0 per CHANGELOG). (c) If a downstream agent/skill hardcodes a tool as experimental, the promotion could silently change discovery semantics. Grep `skills/*/SKILL.md` and prompt files for any mentions of the 25 tools' experimental status; remove or update. |
| **Validation** | `just ci` clean. Full test suite green. `server_info` post-reinstall reports `stable: 102` (or paired number), `experimental: 28` (or fewer). Manual read of catalog resource confirms the promotions. |
| **Performance review** | N/A — tier labels only; no behavior change. |
| **Backlog sync** | Closes `experimental-promotion-batch-2026-04`. Keeps the 2026-04-13 audit report as evidence. Does NOT auto-close the Jellyfin audit row — that one's already `keep-experimental`/`needs-more-evidence` only and isn't about promotion. |

---

## Cross-cutting observations

- **Pattern reuse:** Item 1 and Item 10 both touch `ServerSurfaceCatalog.cs` — sequence them so item 10's catalog flip happens after any item-1 description edits have landed, to keep each PR's diff small.
- **Breaking changes this round:** Item 2 (`symbol-search-payload-meta`) changes response shape for several tools; item 1's `metadataName` member-resolution extension changes behavior from "always empty for members" to "returns member refs" — both should be called out in the CHANGELOG section for the PR that ships them.
- **Perf smells observed but NOT fixed in this batch:**
  - `SymbolResolver.ResolveByMetadataNameAsync` iterates `solution.Projects` serially — O(P) compilations. When we extend it for member lookup, the same iteration happens twice (type pass + member pass). Worth parallelizing with `Task.WhenAll` if profiling shows this as hot; otherwise defer.
  - `CodeMetricsService.GetComplexityMetricsAsync` when `filePaths` is supplied could short-circuit: only compute complexity for methods in those files instead of scanning all methods per project and filtering. Worth a backlog row `code-metrics-filepaths-early-filter` (P4 perf) if the new `filePaths` parameter is measured slow on Jellyfin.
- **One PR per item, 10 PRs total** (item 10 is the only version-bump PR; the others are docs/code/patch without a version bump unless they touch compiled code broadly).
- **Suggested merge order:** 4, 3 (closures) → 8, 9 (docs) → 5 (installer fix) → 6, 7 (small features) → 1, 2 (touching shared catalog + response shapes) → 10 (version bump + NuGet publish to seal the batch).

---

## Final per-PR todo template

```
- [ ] Implement fix per `ai_docs/reports/20260414T171024Z_top10-remediation-plan-v3.md` § <id>
- [ ] Tests pass: `just test`
- [ ] CI gate green: `just ci`
- [ ] For item 10: version bump + NuGet publish via tag push
- [ ] backlog: sync ai_docs/backlog.md (remove closed rows, add sub-rows for split items, update cross-references)
```
