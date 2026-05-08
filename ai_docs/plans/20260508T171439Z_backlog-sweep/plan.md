# Backlog sweep plan - 20260508T171439Z

**Generated:** 2026-05-08T17:14:39Z
**Backlog snapshot:** 2026-05-08T16:41:47Z
**Initiative count:** 12
**Anchor verification:** partial - source anchors were read and the live Roslyn workspace was loaded for semantic checks; destructive or preview-style tool calls were intentionally skipped in the main checkout.
**Addenda loaded:** yes - `ai_docs/prompts/backlog-sweep-addenda.md`
**MCP availability:** `.mcp.json` declares `roslyn` via `roslynmcp`; live `server_info` reported `roslyn-mcp` version `1.34.2+e01c2f97dfc80e6c1a9888aed70c71191d8666c0`, then `workspace_load` loaded `RoslynMcp.slnx` as workspace `83d1b69a76274e2aaa3e4b2a5f3aaa6c` with 5 projects, 583 documents, and 0 workspace diagnostics.

## Initiatives (in order)

### 1. build-test-self-analyzer-file-lock

| Field | Content |
|---|---|
| Status | merged (PR #563, 2026-05-08) |
| Priority | High |
| Backlog rows closed | `build-test-self-analyzer-file-lock` |
| Diagnosis | Confirmed the validation entry points shell out through loaded workspace state: `src/RoslynMcp.Roslyn/Services/BuildService.cs:28` calls `dotnet build`, and `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:44` calls `dotnet test` with existing MSB3027/MSB3021 fast-fail handling. The analyzer is wired into Host.Stdio via `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj:59` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`, so a self-hosted workspace can hold the analyzer output DLL while a child build/test process tries to overwrite it. |
| Approach | Fix the analyzer-lock root in workspace load rather than only masking build/test symptoms. Preferred path: shadow-copy or otherwise detach analyzer references for the loaded self workspace before compiler/analyzer consumers materialize them, so the original analyzer output under `analyzers/ServerSurfaceCatalogAnalyzer/bin/...` can be rebuilt. Keep the BuildService and TestRunnerService file-lock envelope behavior, but add regression coverage proving `build_workspace` no longer fails on this repo. If the shadow-copy path needs more than the scoped files below, stop and split. |
| Scope | Production files touched: 3 - `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, one small helper under `src/RoslynMcp.Roslyn/Helpers/`, and at most one validation service file if the shell path needs an explicit lock-release hook. Test files modified or added: 1 - a self-hosting validation integration test. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | The test must prove the host process no longer holds the analyzer output, not just that one command retries. Shadow copies must not hide analyzer diagnostics, stale analyzer versions, or workspace reload behavior. |
| Validation | Focused regression invoking `build_workspace` or the underlying `BuildService.BuildWorkspaceAsync` against `RoslynMcp.slnx` or an equivalent self-hosting fixture; then `mcp__roslyn__compile_check`, related tests for `WorkspaceManager` and validation services, `./eng/verify-ai-docs.ps1`, and `./eng/verify-release.ps1 -Configuration Release -NoCoverage`. |
| Performance review | Hot path only at workspace load. Measure the added copy/detach cost and keep it bounded to analyzer-reference projects. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed self-hosted build/test validation so loading this repo through Roslyn MCP no longer leaves the server-surface analyzer DLL locked for child `dotnet build` or `dotnet test` runs. |
| Backlog sync | Close rows: `build-test-self-analyzer-file-lock`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 2. find-references-duplicate-metadata-candidates

| Field | Content |
|---|---|
| Status | merged (PR #565, 2026-05-08) |
| Priority | Medium |
| Backlog rows closed | `find-references-duplicate-metadata-candidates` |
| Diagnosis | `find_references` delegates metadata-name disambiguation through `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:190` and then calls `ReferenceService.FindReferencesAsync` at `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:202`. Candidate enumeration in `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:197` dedupes with `SymbolEqualityComparer.Default`, which can still treat equivalent source declarations from separate project compilations as distinct. |
| Approach | Add a stable candidate dedupe key for metadata-name lookup before ambiguity envelopes are emitted. Key on metadata display, source file path, source span, and project/assembly where needed; collapse same source declaration duplicates while preserving real overload/member ambiguity. |
| Scope | Production files touched: 2 - `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs`, and possibly `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` only if envelope text needs a count clarification. Test files modified or added: 1 - extend `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs` or add a focused metadata-name test. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Over-deduping must not collapse overloads, partial declarations in different source spans, or member-vs-type collisions that should still elicit. |
| Validation | Regression using `metadataName=RoslynMcp.Roslyn.Services.WorkspaceManager` that no longer returns duplicate same-span candidates; keep existing ambiguous `System.String.Format` coverage. Run `mcp__roslyn__compile_check`, focused symbol-disambiguation tests, `./eng/verify-ai-docs.ps1`, and release validation if code changes land. |
| Performance review | N/A - candidate list is small and only runs on metadata-name disambiguation. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed metadata-name disambiguation so duplicate compilation candidates for the same source declaration do not produce a false ambiguous `find_references` response. |
| Backlog sync | Close rows: `find-references-duplicate-metadata-candidates`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 3. add-project-reference-self-reference-preview

| Field | Content |
|---|---|
| Status | merged (PR #567, 2026-05-08) |
| Priority | Medium |
| Backlog rows closed | `add-project-reference-self-reference-preview` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:112` resolves both projects and only checks whether the relative `ProjectReference` already exists; it does not reject `project.Id == referencedProject.Id` before diff creation. The tool wrapper at `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs:70` forwards the request directly. |
| Approach | Add pre-diff validation in `PreviewAddProjectReferenceAsync`: reject same project identity and then detect simple cycles by walking the target project's existing `ProjectReferences` graph before adding the new edge. Return an actionable invalid-operation envelope through the existing tool error path. |
| Scope | Production files touched: 1 - `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Cycle detection should use Roslyn project IDs, not only file names, so SDK path normalization and case differences do not produce false negatives. |
| Validation | Tests for self-reference rejection, valid cross-project reference success, and a two-project cycle rejection. Run `mcp__roslyn__compile_check`, focused project mutation tests, and `./eng/verify-ai-docs.ps1`; run release validation for the code PR. |
| Performance review | N/A - project graph walk is tiny and only runs for project-reference preview. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `add_project_reference_preview` to reject self-references and obvious project-reference cycles before generating a diff. |
| Backlog sync | Close rows: `add-project-reference-self-reference-preview`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 4. find-overrides-interface-root-empty

| Field | Content |
|---|---|
| Status | merged (PR #569, 2026-05-08) |
| Priority | Medium |
| Backlog rows closed | `find-overrides-interface-root-empty` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:141` promotes implementation-site symbols before `SymbolFinder.FindOverridesAsync`, and `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:242` maps implicit implementation symbols back to their interface member. The interface-member root itself still passes directly to `SymbolFinder.FindOverridesAsync`, which can miss implicit implementations in the audit fixture even though `find_base_members` can see the reverse relation. |
| Approach | Extend the interface-member root path in `FindOverridesAsync`: when the resolved symbol is an interface method, property, or event, enumerate implementing types in the solution and use `FindImplementationForInterfaceMember` to collect implicit implementations. Merge those with existing `FindOverridesAsync` results and keep the stable `SymbolDto` ordering. |
| Scope | Production files touched: 1 - `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`. Test files modified or added: 1 - extend `tests/RoslynMcp.Tests/P4BehavioralBundleTests.cs` or add a targeted reference-service test. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Must avoid duplicate entries when Roslyn already returns an implementation for some interface shapes. Metadata-boundary interfaces should keep existing `FilePath=null` behavior where applicable. |
| Validation | Fixture with an interface member and one implicit implementation: `find_overrides` from the interface member returns the implementation and agrees with `find_base_members` symmetry. Run `mcp__roslyn__compile_check`, focused reference-service tests, and release validation if code lands. |
| Performance review | Potentially solution-wide type walk for interface roots. Keep it scoped to interface-member queries and reuse Roslyn symbol APIs rather than text search. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `find_overrides` so interface-member roots include implicit implementations instead of returning an empty result set. |
| Backlog sync | Close rows: `find-overrides-interface-root-empty`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 5. symbol-relationships-return-token-bucket-mix

| Field | Content |
|---|---|
| Status | pending |
| Priority | Medium |
| Backlog rows closed | `symbol-relationships-return-token-bucket-mix` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:144` promotes a return-type token to the declaring member for the response symbol, but `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:154` through `:157` still calls reference, implementation, base, and override services with the original locator. The wrapper at `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:389` advertises the promoted-member behavior. |
| Approach | After promotion, create a promoted locator or route child bucket calls through service overloads that accept the promoted symbol. Ensure `symbol`, definitions, references, implementations, base members, and overrides all describe the same target. |
| Scope | Production files touched: 1 - `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs`. Test files modified or added: 1 - extend `tests/RoslynMcp.Tests/SemanticExpansionTests.cs` or add a symbol-relationships regression. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Do not regress `preferDeclaringMember=false`, metadata-name locators, or literal type-token inspection. |
| Validation | Regression on a return-type token such as `Task<WorkspaceStatusDto>` in `WorkspaceManager.LoadAsync`: the response symbol and all relationship buckets describe `LoadAsync`, not `Task`. Run `mcp__roslyn__compile_check`, focused symbol relationship tests, and release validation if code lands. |
| Performance review | N/A - reusing the promoted target should not add new traversals beyond the existing bucket calls. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `symbol_relationships` so return-type-token promotion applies consistently to every relationship bucket. |
| Backlog sync | Close rows: `symbol-relationships-return-token-bucket-mix`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 6. scaffold-test-batch-nullable-constructor-output

| Field | Content |
|---|---|
| Status | pending |
| Priority | Medium |
| Backlog rows closed | `scaffold-test-batch-nullable-constructor-output` |
| Diagnosis | Batch scaffolding resolves target constructor args at `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:92` and `:1078`. `BuildArgExpression` at `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:1166` uses the display name of the nullable symbol directly, so nullable concrete classes can become invalid expressions like `new WorkspaceManagerOptions?()`. Existing tests cover normal concrete and interface args, not nullable concrete parameters. |
| Approach | Normalize nullable annotations before constructibility checks and object creation text. Emit `new WorkspaceManagerOptions()` when the underlying class has an accessible parameterless constructor; otherwise emit a compile-safe `default(WorkspaceManagerOptions?)` or TODO placeholder consistent with existing collaborator handling. Apply the same fix to single and batch scaffold paths because they share `BuildArgExpression`. |
| Scope | Production files touched: 1 - `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Nullable value types and nullable reference types should remain distinct. NSubstitute behavior for nullable interface collaborators should stay compile-safe. |
| Validation | Regression covering nullable concrete with parameterless ctor, nullable concrete without parameterless ctor, and nullable interface. Generated snippets must compile via `mcp__roslyn__compile_check` or an isolated workspace compile. Run focused scaffolding tests and release validation for the code PR. |
| Performance review | N/A - local symbol formatting only. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed test scaffolding constructor-argument synthesis for nullable concrete parameter types. |
| Backlog sync | Close rows: `scaffold-test-batch-nullable-constructor-output`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 7. scaffold-test-internal-target-accessibility

| Field | Content |
|---|---|
| Status | pending |
| Priority | Medium |
| Backlog rows closed | `scaffold-test-internal-target-accessibility` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:329` builds a scaffold for the requested target, and `ResolveTargetMethod` only warns for private methods at `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:1121`. Internal target types and methods are treated as directly callable even when the test assembly lacks `InternalsVisibleTo`, leading to CS0122 after apply. |
| Approach | During target resolution, compare target assembly accessibility against the destination test assembly. If an internal type or method is not visible, return a warning and a non-applicable or placeholder-only preview rather than a compiling-looking call. Allow success when `InternalsVisibleTo` already grants access. |
| Scope | Production files touched: 1 - `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Do not block legitimate same-assembly test projects or existing `InternalsVisibleTo` setups. Placeholder output must be explicit enough for agents to act on. |
| Validation | Fixture for inaccessible internal target rejection or warning-with-placeholder, public target success, and `InternalsVisibleTo` success. Run `mcp__roslyn__compile_check`, focused scaffolding tests, and release validation. |
| Performance review | N/A - symbol accessibility checks are local to the target and test compilation. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `scaffold_test_preview` so inaccessible internal targets do not generate tests that fail with CS0122. |
| Backlog sync | Close rows: `scaffold-test-internal-target-accessibility`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 8. validate-recent-git-changes-timeout

| Field | Content |
|---|---|
| Status | pending |
| Priority | Medium |
| Backlog rows closed | `validate-recent-git-changes-timeout` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:60` collects git changes and then runs compile, diagnostics, related-test discovery, and optional tests. `CollectGitChangedFilesAsync` starts `git status` at `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:357` and waits with the caller token at `:383`, but there is no internal progress or phase timeout envelope for slow validation phases. The wrapper at `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:64` catches most exceptions but lets `OperationCanceledException` propagate, matching the timeout symptom. |
| Approach | Add bounded per-phase timeouts or progress checkpoints inside `ValidateRecentGitChangesAsync` and its git subprocess helper. On timeout, return a structured retryable validation result that includes the git-derived changed-file set and the phase that exceeded budget, instead of allowing a bare client timeout. |
| Scope | Production files touched: 2 - `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` and `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` if the tool envelope needs timeout classification. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` or `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 45000 |
| Risks | Cooperative cancellation from the MCP client must still propagate where appropriate; internal validation budget expiry should be distinguishable from caller cancellation. |
| Validation | Tests for clean run within budget and deliberately slow subprocess or validation phase returning retryable timeout envelope with changed-file set populated. Run `mcp__roslyn__compile_check`, focused validation tests, `./eng/verify-ai-docs.ps1`, and release validation. |
| Performance review | The timeout check is in validation tooling. Avoid extra work on the clean path; only add timing guards and compact progress fields. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `validate_recent_git_changes` timeout behavior so slow validation phases return a structured retryable result with the changed-file set. |
| Backlog sync | Close rows: `validate-recent-git-changes-timeout`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 9. promotion-scorecard-20260427-review

| Field | Content |
|---|---|
| Status | pending |
| Priority | Low |
| Backlog rows closed | `promotion-scorecard-20260427-review` |
| Diagnosis | `docs/experimental-promotion-analysis.md:58` still says to repopulate Tier 1 candidates after the next audit rollup, while `.claude/skills/promote-tier/SKILL.md:16` documents the newer aggregated scorecard quorum workflow. Catalog samples show many formerly listed candidates are no longer pending in the same form, such as `get_operations` already stable in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs:47`, while `server_catalog_tools_page` remains experimental in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Resources.cs:9`. |
| Approach | Write a bounded decision note at `ai_docs/items/promotion-scorecard-20260427-review.md` that reconciles the 2026-04-27 candidate list against the current catalog and the new quorum process. Split only accepted concrete tier flips into fresh backlog rows; mark rejected or superseded candidates in the note. |
| Scope | Production files touched: 0. Docs files touched: 1 new decision note plus `ai_docs/backlog.md` only during implementation closeout. Test files modified or added: 0. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | Do not flip catalog tiers in this initiative. This is a decision pass only; actual promotions need their own rows and release-gated validation. |
| Validation | `./eng/verify-ai-docs.ps1` and a manual catalog cross-check using `server_info` or catalog source. |
| Performance review | N/A - docs-only decision note. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Recorded the 2026-04-27 promotion-scorecard review against the current catalog and quorum-based promote-tier workflow. |
| Backlog sync | Close rows: `promotion-scorecard-20260427-review`; add follow-on rows only for accepted tier flips. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 10. dry-run-preview-side-effect-audit

| Field | Content |
|---|---|
| Status | pending |
| Priority | Low |
| Backlog rows closed | `dry-run-preview-side-effect-audit` |
| Diagnosis | Preview wrappers such as `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs:23` are read-side entry points, while apply paths in `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:292` and `src/RoslynMcp.Roslyn/Services/EditService.cs:36` clearly mutate workspace and disk state. The backlog evidence is explicitly weak, so the next deliverable should prove or reject side effects rather than change tool behavior blindly. |
| Approach | Add an investigation note under `ai_docs/items/dry-run-preview-side-effect-audit.md` and focused tests around representative preview calls. Capture workspace version, workspace changes, project file bytes, and cache state before and after preview-only operations. If a real mutation is confirmed, split a targeted implementation row for the affected tool family and leave this row closed by the evidence note. |
| Scope | Production files touched: 0 unless a confirmed failing test needs a minimal test hook. Docs files touched: 1 investigation note. Test files modified or added: 1 focused preview-side-effect test file. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Tests must distinguish expected preview-store token creation from workspace/disk mutation. Avoid asserting brittle implementation details of Roslyn immutable solution snapshots. |
| Validation | Focused tests proving representative preview calls do not change workspace version or on-disk files, or a pinned failing case plus the investigation note. Run `mcp__roslyn__compile_check`, focused tests, and `./eng/verify-ai-docs.ps1`. |
| Performance review | N/A - audit/test work only. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Audited preview-only tool calls for workspace or disk side effects and recorded the follow-up decision. |
| Backlog sync | Close rows: `dry-run-preview-side-effect-audit` if no side effect is confirmed; otherwise close it after adding targeted implementation rows. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 11. change-signature-reorder-preview

| Field | Content |
|---|---|
| Status | pending |
| Priority | Low |
| Backlog rows closed | `change-signature-reorder-preview` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:89` supports only `add`, `remove`, and `rename`, and `:95` explicitly refuses reorder. The wrapper at `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs:23` documents the same limitation. The existing add/remove builder at `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:12` already rewrites declarations and call sites in one preview, so a bounded reorder implementation can reuse that pattern. |
| Approach | Implement `op="reorder"` for method parameters using a caller-supplied ordered parameter-name list or positions. Reorder the declaration and every invocation argument list semantically, preserving named arguments where possible and refusing ambiguous default/ref/out/params cases if needed. If the executor judges permanent non-support is preferable, it must produce a docs/test assertion instead, but the default plan is first-class reorder support. |
| Scope | Production files touched: 3 - `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`, `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs` or a sibling helper, and `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 45000 |
| Risks | Call-site fanout can exceed a small fixture if tested against production symbols; keep regression fixtures small. Reordering must preserve semantic argument mapping for positional and named calls. |
| Validation | Reorder two parameters on a sample method and verify positional and named call sites update correctly. Add negative coverage for unsupported shapes as needed. Run `mcp__roslyn__compile_check`, focused change-signature tests, and release validation. |
| Performance review | The call-site rewrite walks callers like existing add/remove. No new hot path beyond preview invocation. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added `change_signature_preview` support for parameter reordering with declaration and call-site updates. |
| Backlog sync | Close rows: `change-signature-reorder-preview`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 12. parameter-object-preview-tool

| Field | Content |
|---|---|
| Status | pending |
| Priority | Low |
| Backlog rows closed | `parameter-object-preview-tool` |
| Diagnosis | The canonical design note `ai_docs/items/parameter-object-preview-design.md` defines v1 as a positional-record grouping tool and sizes it as 4 structural units plus mandatory addenda. Current catalog `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs:18` has `change_signature_preview` but no `parameter_object_preview` entry, and `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs:80` delegates Roslyn service registration without a parameter-object service today. |
| Approach | Implement v1 exactly from the design note: new Core request/service contract, Roslyn implementation that creates a record DTO and rewrites call sites atomically, Host.Stdio tool wrapper, catalog registration, DI registration, README surface-count update, and test fixture DI updates. Reuse the existing `apply_refactoring` path for token redemption. |
| Scope | Rule 3 exemption: new MCP tool structural-unit shape from addenda, 4 structural units. Production files touched: 9 - `src/RoslynMcp.Core/Services/IParameterObjectService.cs`, `src/RoslynMcp.Core/Models/ParameterObjectPreviewRequest.cs`, `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`, `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs`, `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`, `tests/RoslynMcp.Tests/TestBase.cs`, `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`, and `README.md`. Test files modified or added: 1 - `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 70000 |
| Risks | Cross-project visibility and missing project references are the highest-risk parts. Do not auto-insert project references in v1. Keep default-value and by-ref refusals explicit and structured. |
| Validation | Seven-case regression set from the design note: positional grouping, named and mixed call sites, default-value refusal, ref/out refusal, cross-project success when reference exists, cross-project refusal when missing, and `apply_refactoring` writing the new file plus rewritten call sites. Run `mcp__roslyn__compile_check`, focused parameter-object tests, `./eng/verify-ai-docs.ps1`, `./eng/verify-release.ps1 -Configuration Release -NoCoverage`, and the NuGet vulnerability audit before ship. |
| Performance review | Preview-time caller walk only. Keep the implementation bounded to target method callers and avoid solution-wide text scans. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added experimental `parameter_object_preview` support for grouping method parameters into a positional record DTO and rewriting call sites atomically. |
| Backlog sync | Close rows: `parameter-object-preview-tool`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

## Items skipped

| Backlog row | Reason |
|---|---|
| `file-lock-aware-prompt-validation-guidance` | Skipped because `deps` references unfinished `build-test-self-analyzer-file-lock`; plan it after initiative 1 ships. |
| `tool-surface-pagination-or-tool-sets` | Skipped because its trigger is not met: live `server_info` reported 168 tools, below the row's approximate 200-tool threshold, and no current small-model friction evidence was cited. |
| `validate-locator-preflight-tool` | Skipped because it is parked in Defer and says to re-evaluate after 2026-05-12; current run date is 2026-05-08. |
| `http-streamable-host-project` | Skipped because it is parked in Defer pending a concrete remote-deployment driver. |
| `workspace-process-pool-or-daemon` | Skipped because it is parked in Defer pending future worse-profile evidence. |

## Self-vet

- No initiative closes more than one backlog row.
- Every non-structural initiative stays within the 4 production file cap and 3 test file cap.
- The `parameter-object-preview-tool` initiative explicitly uses the repo addenda structural-unit exemption and records the real 9-file count.
- No initiative exceeds 80000 estimated context tokens.
- Every initiative has an explicit `toolPolicy`.
- Fanout estimates are recorded in `state.json`; low-risk local fixes use the scoped production file count, and the new-tool initiative records the structural-unit fanout.
- Source citations are plain inline code paths with line numbers, not markdown links, so the generated plan is repo-doc-checker safe.

## Next step

Run `/backlog-sweep:review` before `/backlog-sweep:execute`.
