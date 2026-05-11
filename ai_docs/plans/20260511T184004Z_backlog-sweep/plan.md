# Backlog sweep plan — 20260511T184004Z

**Generated:** 2026-05-11T18:40:04Z
**Backlog snapshot:** 2026-05-11T18:15:00Z
**Initiative count:** 25 (24 pending + 1 deferred from outset)
**Anchor verification:** performed (cheap initiatives) / skipped (larger refactors)
**Addenda loaded:** yes (`ai_docs/prompts/backlog-sweep-addenda.md`)

**Candidate pool:** 14 Medium + 25 Low - 7 Reserved - 5 Defer = 32 claimable. Capped at 25 per `count=25`. Skipped (lowest priority): `editorconfig-write-no-auto-invalidation`, `find-overrides-interface-root-empty`, `host-middleware-tools-namespace-cycle`, `list-analyzers-totalrules-variance` (weak evidence), `scaffolding-service-split-by-scaffold-type` (weak evidence), `set-project-property-preview-directory-build-props-blindness`, `tool-surface-pagination-or-tool-sets` (weak evidence), `eternal-experimental-age-audit` (low urgency).

**Note on Reserved rows:** 7 rows carry the `**Reserved — [gh #NNN]**` marker (issues #606-#612) and are excluded from this plan per the contributor-pickup convention. They will re-enter sweep eligibility when the marker is removed (after contributor PR lands OR maintainer reclaims).

---

## Initiatives (in order)

### 1. `apply-project-mutation-not-registered-revert`

| Field | Content |
|---|---|
| Status | merged (PR #640, 2026-05-11) |
| Backlog rows closed | `apply-project-mutation-not-registered-revert` |
| Diagnosis | `apply_project_mutation` (in `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`) writes `.csproj` to disk and reports `success:true` but never registers the operation on the `IUndoService` revert stack. `revert_last_apply` therefore returns `reverted:false` — silent data-loss risk in scripted pipelines that rely on the revert-stack invariant. Fanout probe: `IUndoService` is referenced in 17 files; the fix is local (register at the apply site), not cross-cutting. |
| Approach | At the end of the apply branch in `ProjectMutationTools.apply_project_mutation`, call `IUndoService.RecordOperation(...)` with the pre-apply `.csproj` bytes captured before the write. Mirror the pattern in `EditTools.cs` / `MultiFileEditTools.cs` which already do this correctly. |
| Scope | Production: 1 file (`src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`). Tests: 1 new fixture in `tests/RoslynMcp.Tests/Tools/ProjectMutationToolsTests.cs` (or equivalent) — apply, revert, assert the file's pre-apply bytes are restored. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Revert path must capture pre-apply bytes BEFORE the write (race window if captured after); `.csproj` is XML so byte-exact restore matters. |
| Validation | New fixture asserts revert round-trip; `mcp__roslyn__compile_check` after apply+revert; existing `UndoService` tests still pass. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `apply_project_mutation` now registers on the `revert_last_apply` stack — pipelines relying on revert-stack rollback no longer leak `.csproj` mutations. |
| Backlog sync | Close rows: `apply-project-mutation-not-registered-revert`. |

### 2. `test-related-column-required-schema-mismatch`

| Field | Content |
|---|---|
| Status | deferred (diagnosis-premise mismatch — needs re-plan) |
| Backlog rows closed | `test-related-column-required-schema-mismatch` |
| Diagnosis | `test_related` tool schema marks `column` as optional but the server requires it. Callers following the schema get a runtime error. Per the addenda's **tool-surface-only exemption**: this is a schema/wrapper edit on an already-registered tool — Rule 3 exemption applies, ≤2 files. |
| Approach | Option A (preferred): mark `column` as **required** in the catalog entry for `test_related` in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Testing.cs` (or wherever the schema lives). Update the wrapper in `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs` for consistency. |
| Scope | Production: 2 files (catalog + tool wrapper). Tests: 1 fixture covering `test_related(workspaceId, filePath, line)` without `column` — assert schema-rejection before the server is reached. **Rule 3 exemption: tool-surface-only, 2 files.** |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | If existing callers depend on the optional behavior (none observed), this is a breaking schema change — acceptable per project constraints. |
| Validation | New fixture; `mcp__roslyn__compile_check`; existing test-coverage tests still pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `test_related` schema now matches server behavior — `column` parameter is required (was incorrectly marked optional). Closes gh #618. |
| Backlog sync | Close rows: `test-related-column-required-schema-mismatch`. |

### 3. `callers-callees-rejects-fully-qualified-names`

| Field | Content |
|---|---|
| Status | merged (PR #639, 2026-05-11) |
| Backlog rows closed | `callers-callees-rejects-fully-qualified-names` |
| Diagnosis | `callers_callees` returns `NotFound` when `metadataName` includes a full method signature with parameter types; sibling tools (`find_references`, `find_type_mutations`) accept the same fully-qualified form. Vocabulary inconsistency. |
| Approach | In `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (or `src/RoslynMcp.Roslyn/Services/CallerCalleeService.cs`), parse `metadataName` with the same metadata-name parser `find_references` uses. If the find-references parser is in a shared helper, reuse it; otherwise extract a parser helper. |
| Scope | Production: 2 files (tool + service). Tests: 1 fixture calling `callers_callees` with a fully-qualified method name; assert successful symbol resolution. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Parser sharing may surface that find-references uses a private helper — light refactor to extract may be needed (still within 2-file budget). |
| Validation | New fixture; `mcp__roslyn__compile_check`; existing callers-callees tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `callers_callees` now accepts fully-qualified method signatures in `metadataName`, matching `find_references`. Closes gh #616. |
| Backlog sync | Close rows: `callers-callees-rejects-fully-qualified-names`. |

### 4. `get-namespace-dependencies-empty-multiproject`

| Field | Content |
|---|---|
| Status | merged (PR #638, 2026-05-11) |
| Backlog rows closed | `get-namespace-dependencies-empty-multiproject` |
| Diagnosis | `get_namespace_dependencies(circularOnly=true)` returns empty arrays on a 36-project solution — callers can't distinguish "no cycles" from "not analyzed". |
| Approach | Add `analyzedProjectCount` (and optionally `totalNamespacesScanned`) to the response DTO; populate from the analysis loop. Update tool wrapper to emit the new field. |
| Scope | Production: 2 files (`src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs`, `src/RoslynMcp.Host.Stdio/Tools/NamespaceDependencyTools.cs`). Tests: 1 fixture on a multi-project workspace asserts non-zero `analyzedProjectCount`. **Rule 3 exemption: tool-surface-only, 2 files** (response-DTO field addition). |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | DTO addition is non-breaking. Underlying cross-project analysis behavior may legitimately return empty — document if so. |
| Validation | New fixture; `mcp__roslyn__compile_check`; existing namespace-dependency tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `get_namespace_dependencies` response now includes `analyzedProjectCount` so callers can distinguish "no cycles" from "no analysis". Closes gh #615. |
| Backlog sync | Close rows: `get-namespace-dependencies-empty-multiproject`. |

### 5. `parallel-fanout-auto-reload-timeout-floor`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `parallel-fanout-auto-reload-timeout-floor` |
| Diagnosis | Per-call held-time timeouts fire at 5s floor when an auto-reload is in flight, even though completed in <2s held-time post-reload. Two fix paths surfaced in row: (a) extend held-time budget when `staleAction=auto-reloaded`; (b) surface a distinct `WorkspaceStaleReloading` error category. **Touches the `WorkspaceManager.cs` hotspot — schedule at most one hotspot-touching initiative per parallel wave.** |
| Approach | Path (a): in `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`, detect `staleAction=auto-reloaded` state on a held request and extend the deadline by the reload's remaining time. Path (b) is the fallback if (a) introduces too much complexity — surface a new error category in `src/RoslynMcp.Core/Models/`. |
| Scope | Production: 2 files (`WorkspaceExecutionGate.cs` + possibly `WorkspaceManager.cs`). Tests: 1 concurrency fixture simulating reload + parallel reads. |
| Tool policy | edit-only |
| Estimated context cost | 45000 |
| Risks | Concurrency code; race conditions; existing held-time tests must not regress. Hotspot file `WorkspaceManager.cs` — distribute across waves. |
| Validation | New concurrency fixture; existing concurrency tests pass; manual repro via worktree-add during fanout reads. |
| Performance review | Held-time budget extension affects request latency. Verify p50 budget (5s) still applies under non-reload conditions. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (北 draft) | **Fixed:** Parallel read fan-out no longer times out at the 5s floor when a workspace auto-reload is in flight — the held-time budget extends through reload completion. |
| Backlog sync | Close rows: `parallel-fanout-auto-reload-timeout-floor`. |

### 6. `workspace-load-sibling-worktree-sanctioned-root`

| Field | Content |
|---|---|
| Status | merged (PR #637, 2026-05-11) |
| Backlog rows closed | `workspace-load-sibling-worktree-sanctioned-root` |
| Diagnosis | `workspace_load` refuses paths outside the client-sanctioned root. The mcp-server-surface-test skill's disposable worktree at `../<sibling>` is structurally outside the root, forcing Phases 6/9/10/12/13 to `skipped-safety` on every consumer-repo audit. The companion row `prompts-full-md-phase0-worktree-path-sandbox` (#7) is the prompt-side fix; this row is the tool-side fix. |
| Approach | Per row: option (b) — server exposes an `expandSanctionedRoots` flag (operator-opt-in) on `workspace_load` so the skill can widen the allowlist for a specific worktree path. Add to `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs` + wrapper in `WorkspaceTools.cs`. |
| Scope | Production: 2 files (`ClientRootPathValidator.cs`, `WorkspaceTools.cs`). Tests: 1 integration fixture covering rejection without flag + acceptance with flag. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Security boundary — the opt-in flag must be conservative; document that only the operator can pass it. Don't auto-widen on every request. |
| Validation | New fixture; existing root-validator tests pass; manual repro by running surface-test against TradeWise with the new flag. |
| Performance review | N/A. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | **Added:** `workspace_load(expandSanctionedRoots=true)` operator-opt-in to widen the path-validator allowlist for disposable-worktree audit workflows. |
| Backlog sync | Close rows: `workspace-load-sibling-worktree-sanctioned-root`. Related: `prompts-full-md-phase0-worktree-path-sandbox` (companion row, ships separately). |

### 7. `prompts-full-md-phase0-worktree-path-sandbox`

| Field | Content |
|---|---|
| Status | merged (PR #647, 2026-05-11) |
| Backlog rows closed | `prompts-full-md-phase0-worktree-path-sandbox` |
| Diagnosis | Prompt-side patch for `workspace-load-sibling-worktree-sanctioned-root`. The `prompts/full.md` Phase 0 step 2 instructs `git worktree add ../<repo>-surface-test-<ts>` (sibling path, outside sanctioned root). Doc-only fix. |
| Approach | Update `skills/mcp-server-surface-test/prompts/full.md` Phase 0 step 2 to use an inside-repo worktree path (`git worktree add .worktrees/surface-test-<ts>`). No corresponding maintainer-overlay update needed — it's deleted. |
| Scope | Production: 1 file (`skills/mcp-server-surface-test/prompts/full.md`). Tests: prose-only — visual review. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | Existing audit reports cite the old path — purely retrospective. No regression risk. |
| Validation | Manual diff review; verify-skills-are-generic.ps1 still passes. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `/mcp-server-surface-test` Phase 0 worktree path is now under the audited repo root (`.worktrees/surface-test-<ts>`), no longer rejected by `workspace_load`'s sanctioned-root check. Closes gh #614. |
| Backlog sync | Close rows: `prompts-full-md-phase0-worktree-path-sandbox`. |

### 8. `file-lock-aware-prompt-validation-guidance`

| Field | Content |
|---|---|
| Status | in-progress (branch: remediation/file-lock-aware-prompt-validation-guidance, worktree: .worktrees/file-lock-aware-prompt-validation-guidance) |
| Backlog rows closed | `file-lock-aware-prompt-validation-guidance` |
| Diagnosis | Phase 8 `build_workspace`/`test_run` and the `debug_test_failure` prompt path repeatedly retry full build/test validation against the self-hosted workspace, hitting `MSB3027`/`MSB3021` file-lock errors. Per the row's anchors, the prompt rendering paths in `PromptMessageBuilder.cs` and `RoslynPrompts.RefactoringWorkflows.cs` need file-lock awareness. |
| Approach | Update prompt templates and the dispatcher to recognize `failureEnvelope.errorKind=FileLock` (or `MSB3027`/`MSB3021` text in the failure message) as infrastructure rather than test failure. Add a bypass-guidance section that tells operators to close/reload the workspace or run validation from an isolated process after `dotnet build-server shutdown`. |
| Scope | Production: 3 files (`.claude/skills/mcp-server-stress/prompts/maintainer-overlay.md` — wait, this file was DELETED in PR #633. Update to: `skills/mcp-server-surface-test/prompts/full.md` + `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs` + `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs`). Tests: 1 prompt-rendering test that exercises the file-lock failure envelope path. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Backlog row anchors are stale post-PR-#633 (cites the deleted `maintainer-overlay.md`). Executor must rewrite to the canonical `full.md` path. **Anchor flagged stale: `cited anchor not found; executor may use synthetic examples`.** |
| Validation | New prompt-render test; manual run via `debug_test_failure` against a file-locked workspace. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** Audit prompt and `debug_test_failure` now recognize file-lock failures (`MSB3027`/`MSB3021`) as infrastructure and surface bypass guidance (close/reload workspace or use `dotnet build-server shutdown`) instead of retrying the build/test loop. |
| Backlog sync | Close rows: `file-lock-aware-prompt-validation-guidance`. Flag the row's anchors as needing re-intake on the next sweep (legacy maintainer-overlay.md reference). |

### 9. `fix-all-preview-provider-crash-ide0305`

| Field | Content |
|---|---|
| Status | merged (PR #642, 2026-05-11) |
| Backlog rows closed | `fix-all-preview-provider-crash-ide0305` |
| Diagnosis | `fix_all_preview(diagnosticId=IDE0305)` crashes with `Sequence contains no elements` — same crash class previously documented for IDE0300. The vulnerable-provider class is wider than one ID. |
| Approach | In `src/RoslynMcp.Roslyn/Services/FixAllService.cs`, wrap the provider invocation in try/catch that catches `InvalidOperationException` with `"Sequence contains no elements"` and promotes to `perOccurrenceFallback`. Audit the file for other diagnostic IDs that route through the same code path. |
| Scope | Production: 2 files (`FixAllService.cs`, `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs` for the response envelope update). Tests: 1 fixture asserting graceful `perOccurrenceFallback` promotion for IDE0305. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | The catch must be narrow — don't swallow other `InvalidOperationException`s. Existing fix-all tests must not regress. |
| Validation | New fixture; `mcp__roslyn__compile_check`; existing FixAll tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `fix_all_preview(IDE0305)` no longer crashes with `FixAllProviderCrash`; the `InvalidOperationException: Sequence contains no elements` path now auto-promotes to `perOccurrenceFallback`. |
| Backlog sync | Close rows: `fix-all-preview-provider-crash-ide0305`. |

### 10. `find-consumers-static-class-classification`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-consumers-static-class-classification` |
| Diagnosis | `find_consumers` classifies consumers of a `public static class` as `dependencyKinds=["Other"]` (uninformative); `find_type_consumers` classifies them as `kinds=["local"]` (incorrect — no `var x = StaticClass...`). Static-method invocation is a very common pattern; the uninformative bucket makes both tools weak for static-class targets. |
| Approach | In `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs`, add `Invocation` (or `StaticReference`) kind detection — when the consumer's syntax is `<StaticClass>.<Member>(...)` or a using statement, classify as `Invocation` rather than `Other`. Align the vocabularies between the two tools. |
| Scope | Production: 3 files (`ConsumerAnalysisService.cs`, `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs`, `src/RoslynMcp.Core/Models/ConsumerAnalysisDto.cs` for the DTO enum addition). Tests: 1 fixture covering `public static class` target. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Vocabulary alignment between the two tools — verify the existing `kinds` enum in `find_type_consumers` accepts a new value without breaking existing consumers. |
| Validation | New fixture; existing consumer-analysis tests pass; assert `dependencyKinds` includes `Invocation` not `Other` for static-class targets. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `find_consumers` and `find_type_consumers` now classify static-class consumers as `Invocation`/`StaticReference` instead of the uninformative `Other`/`local` buckets. |
| Backlog sync | Close rows: `find-consumers-static-class-classification`. |

### 11. `get-syntax-tree-maxtotalbytes-not-enforced`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-syntax-tree-maxtotalbytes-not-enforced` |
| Diagnosis | `get_syntax_tree(maxTotalBytes=40000)` returned a 109 KB payload — the byte budget is ignored. The walker accumulates without checking the running total against the cap. |
| Approach | In `src/RoslynMcp.Roslyn/Services/SyntaxService.cs`, thread a `runningBytes` counter through the recursive walker; truncate when it exceeds `maxTotalBytes`. Return a `truncated: true` flag in the response. |
| Scope | Production: 2 files (`SyntaxService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SyntaxTools.cs` for the response envelope). Tests: 1 fixture calling on a large method with `maxTotalBytes=5000`; assert response ≤5500 bytes. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Truncation point must be valid JSON — close out the response cleanly even mid-traversal. |
| Validation | New fixture; existing syntax-tree tests pass; assert truncation flag is set when applicable. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `get_syntax_tree` now honors the `maxTotalBytes` budget — output truncates within ≤10% of the requested ceiling instead of overflowing unboundedly. |
| Backlog sync | Close rows: `get-syntax-tree-maxtotalbytes-not-enforced`. |

### 12. `reconcile-backlog-vs-github-issues`

| Field | Content |
|---|---|
| Status | merged (PR #643, 2026-05-11) |
| Backlog rows closed | `reconcile-backlog-vs-github-issues` |
| Diagnosis | No automation reconciles backlog rows against GitHub Issue state. PR closes Issue → backlog row remains; manual issue close → row remains; Reserved + closed issue = zombie row; etc. Surfaced 2026-05-11 adversarial audit. **New skill** — single-file initiative. |
| Approach | Create `.claude/skills/reconcile-backlog-vs-issues/SKILL.md` that: (1) scans backlog.md for `[gh #NNN]` references (both `**Reserved**` and tracked-only flavors); (2) queries `gh issue view N --json state,closed,closedAt,updatedAt,labels` per reference; (3) classifies each into 5 states (issue-closed-row-open, issue-closed-row-reserved, reserved-stale, label-drift, issue-reopened-row-missing); (4) emits a triage report. Read-only — does not auto-edit backlog. |
| Scope | Production: 1 file (NEW `.claude/skills/reconcile-backlog-vs-issues/SKILL.md`). Tests: 1 fixture per classification (5 small unit tests in `tests/RoslynMcp.Tests/Skills/ReconcileBacklogVsIssuesSkillTests.cs` if applicable, or prose-only validation). |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | `gh issue view` rate limits with many rows — page or batch. The skill is read-only so no destructive failure mode. |
| Validation | Manual invocation against current backlog (which has many `[gh #NNN]` references); spot-check classifications. |
| Performance review | N/A. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | **Added:** `/reconcile-backlog-vs-issues` maintainer skill — audits every `gh #NNN` reference in `ai_docs/backlog.md` against live Issue state and emits a 5-state triage report (issue-closed-row-open, issue-closed-row-reserved, reserved-stale, label-drift, issue-reopened-row-missing). |
| Backlog sync | Close rows: `reconcile-backlog-vs-github-issues`. |

### 13. `sweep-executor-pr-collision-check`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `sweep-executor-pr-collision-check` |
| Diagnosis | `/backlog-sweep:execute` Step 2 currently checks only for the `**Reserved**` marker. It does NOT query `gh pr list` to detect open contributor PRs targeting an initiative's anchor files. Race: sweep claims a row a contributor has started. Doc-only change to the sweep executor prompt. |
| Approach | In `ai_docs/prompts/backlog-sweep-execute.md` Step 2, after the reservation re-check, add a query: `gh pr list --state open --json number,headRefName,files --jq '.[] | select(.files[].path | IN(<initiative anchors>))'`. If any open PR's diff touches an anchor file, mark the initiative `obsolete` with reason. |
| Scope | Production: 1 file (`ai_docs/prompts/backlog-sweep-execute.md`). Tests: prose-only — visual review. (Hard to unit-test a prompt addition.) |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | False positives if the PR has been open and abandoned. Mitigation: only flag PRs whose `updatedAt` is within 14 days. |
| Validation | Manual review; simulate by opening a draft PR for an initiative's anchor file and running `/backlog-sweep:execute` (would mark obsolete). |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | **Maintenance:** `/backlog-sweep:execute` Step 2 defense-in-depth now also queries `gh pr list` for open contributor PRs touching the initiative's anchor files — initiatives with file collisions are marked `obsolete` before claim. |
| Backlog sync | Close rows: `sweep-executor-pr-collision-check`. |

### 14. `promote-tier-supports-prompts-and-resources`

| Field | Content |
|---|---|
| Status | deferred (design note required before implementation) |
| Backlog rows closed | (none — re-evaluate after design lands) |
| Diagnosis | `/promote-tier` only handles tools. The attribute-vs-no-attribute decision for prompts/resources shapes the implementation. Backlog row explicitly says "Design note required at `ai_docs/items/promote-tier-prompts-and-resources-design.md` before any code change." |
| Approach | Phase 1: write design note at `ai_docs/items/promote-tier-prompts-and-resources-design.md` covering: (a) extend skill to catalog-only path vs (b) introduce attribute markers. Phase 2 (separate row after design lands): implement. |
| Scope | This batch: design note only (1 file). |
| Tool policy | edit-only |
| Estimated context cost | 20000 (design note) |
| Risks | Design pass takes longer than implementation. |
| Validation | Maintainer review of the design note. |
| Performance review | N/A. |
| CHANGELOG category | (none until implementation lands) |
| CHANGELOG entry (draft) | (N/A — deferred). |
| Backlog sync | Update row: switch from "design note required" to "design landed, implementation ready" once Phase 1 ships. |

### 15. `workspace-reloaded-during-call-conflates-notfound`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `workspace-reloaded-during-call-conflates-notfound` |
| Diagnosis | `get_source_text(bad-path)` concurrent with idempotent `workspace_load` reload returns `category=WorkspaceReloadedDuringCall` instead of `NotFound`. The implementation detail leaks into the caller's routing. **Touches `WorkspaceManager.cs` — hotspot; schedule one hotspot initiative per wave.** |
| Approach | In `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`, after the reload completes, re-evaluate the original request before returning the held error. If the file still doesn't exist, return `NotFound` instead of `WorkspaceReloadedDuringCall`. |
| Scope | Production: 2 files (`WorkspaceExecutionGate.cs`, possibly `WorkspaceManager.cs`). Tests: 1 concurrency fixture invoking `workspace_load` + `get_source_text` (bad path) simultaneously; assert `category=NotFound`. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Concurrency code; race conditions. Hotspot `WorkspaceManager.cs` — don't parallel-wave with initiative #5 (`parallel-fanout-auto-reload-timeout-floor`). |
| Validation | New concurrency fixture; existing tests pass. |
| Performance review | Re-evaluation adds one disk-existence check post-reload — sub-millisecond. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `get_source_text(bad-path)` raced against `workspace_load` now returns `category=NotFound` (the true cause) instead of the implementation-detail `WorkspaceReloadedDuringCall`. Closes gh #628. |
| Backlog sync | Close rows: `workspace-reloaded-during-call-conflates-notfound`. |

### 16. `symbol-search-pagination`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-search-pagination` |
| Diagnosis | `symbol_search` has no pagination — broad queries overflow MCP transport cap (69K+ responses) on large solutions. Sibling tools (`list_analyzers`, `test_discover`) have pagination. |
| Approach | Add `offset` (default 0) and `limit` (default 50, max 200) parameters to `symbol_search`; include `totalCount` and `hasMore` in response. Mirror `list_analyzers` pagination contract. |
| Scope | Production: 2 files (`src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`, `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs`). Tests: 1 fixture covering pagination + 1 fixture covering overflow-without-pagination. **Rule 3 exemption: tool-surface-only (parameter addition + DTO pagination), 2 files.** |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Pagination changes response shape — verify catalog entry's parameter schema updated. |
| Validation | New fixtures; `mcp__roslyn__compile_check`; existing symbol-search tests pass. |
| Performance review | Pagination is a perf-win for broad queries (capped response size). |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | **Added:** `symbol_search` now supports `offset` / `limit` pagination (max 200) with `totalCount` and `hasMore` in the response envelope. Closes gh #617. |
| Backlog sync | Close rows: `symbol-search-pagination`. Related: `tool-surface-pagination-or-tool-sets` (broader concern still tracked). |

### 17. `scaffold-test-preview-missing-usings`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `scaffold-test-preview-missing-usings` |
| Diagnosis | `scaffold_test_preview` generates test stubs without `using` directives for constructor-param types from multiple namespaces. Result: 7 CS0246 errors when the constructor takes args from non-default namespaces. **Touches `ScaffoldingService.cs` hotspot (2521 LOC, also tracked by `scaffolding-service-split-by-scaffold-type`).** |
| Approach | In `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` (test-preview path; framework builders at lines 760/820/874), collect namespaces from constructor parameter types and emit `using` directives at the top of the generated file. |
| Scope | Production: 1 file (`ScaffoldingService.cs`). Tests: 1 fixture scaffolding a test for a class with constructor params from ≥3 namespaces; assert the generated file compiles. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Hotspot file (2521 LOC) — make a focused edit, don't touch other scaffolding paths. |
| Validation | New fixture compiles via `mcp__roslyn__compile_check`; existing scaffolding tests pass. |
| Performance review | N/A — already collecting types; just emit usings. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `scaffold_test_preview` now emits `using` directives for all constructor-parameter namespaces — generated test files compile out of the box. Closes gh #624. |
| Backlog sync | Close rows: `scaffold-test-preview-missing-usings`. |

### 18. `set-conditional-property-error-msg-quoting`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `set-conditional-property-error-msg-quoting` |
| Diagnosis | `set_conditional_property_preview` requires MSBuild-style single-quoting (`'$(Configuration)' == 'Release'`) but its error message doesn't mention the required syntax. First-time callers fail with a cryptic message. |
| Approach | In `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`, update the error-message string in the conditional-property validator to include the expected format: *"Use MSBuild-style quoting: `'$(Configuration)' == 'Release'`"*. |
| Scope | Production: 1 file (`ProjectMutationTools.cs`). Tests: 1 fixture asserts the error message contains the literal example. **Rule 3 exemption: tool-surface-only, 1 file.** |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | Error-message text — easy to verify; no behavior change. |
| Validation | New fixture; existing project-mutation tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `set_conditional_property_preview` error message now includes the MSBuild-quoting example (`'$(Configuration)' == 'Release'`) so first-time callers see the expected syntax. Closes gh #622. |
| Backlog sync | Close rows: `set-conditional-property-error-msg-quoting`. |

### 19. `extract-and-wire-interface-duplicate-cross-project`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-and-wire-interface-duplicate-cross-project` |
| Diagnosis | `extract_and_wire_interface_preview` generates a duplicate interface when the target type already implements a cross-project interface with the same name. No check of the implementing type's existing interface list. |
| Approach | In `src/RoslynMcp.Roslyn/Services/ExtractInterfaceService.cs`, before generating, inspect the target type's `Interfaces` collection. If an interface with the same name exists (even in another project), decline with a structured message pointing the caller at `extract_interface_cross_project_preview`. |
| Scope | Production: 2 files (`ExtractInterfaceService.cs`, `src/RoslynMcp.Host.Stdio/Tools/ExtractInterfaceTools.cs`). Tests: 1 fixture with a type that already implements `IFoo` from another project; assert structured rejection. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | The check should be name-only (not full-qualified) since the row says "same name even in different project" is the collision case. |
| Validation | New fixture; existing extract-interface tests pass. |
| Performance review | N/A — one lookup. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `extract_and_wire_interface_preview` now detects when the target type already implements an interface with the same name (cross-project or in-project) and declines with a pointer at `extract_interface_cross_project_preview`. Closes gh #625. |
| Backlog sync | Close rows: `extract-and-wire-interface-duplicate-cross-project`. |

### 20. `move-type-to-file-rejects-single-type`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `move-type-to-file-rejects-single-type` |
| Diagnosis | `move_type_to_file_preview` rejects single-type source files with a wrong-fact error message about nested types. Eliminates the common "rename misnamed file" use case. |
| Approach | Per row option B (cheaper, lower risk): in `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`, improve the error message to point callers at `move_file_preview` for rename-style operations. (Option A — allow the move for rename-style — is a larger behavior change; pick A only if the maintainer prefers.) |
| Scope | Production: 2 files (`TypeMoveService.cs`, `src/RoslynMcp.Host.Stdio/Tools/MoveTypeTools.cs`). Tests: 1 fixture asserts the improved error message contains the alternative pointer. **Rule 3 exemption: tool-surface-only, 2 files.** |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Path B is doc-only behavior preserved. Path A (changing semantics) would need careful review; not selected here. |
| Validation | New fixture; existing move-type tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `move_type_to_file_preview` error message on single-type source files now points callers at `move_file_preview` (the correct tool for rename-style operations) instead of the misleading "nested types cannot be extracted" message. Closes gh #626. |
| Backlog sync | Close rows: `move-type-to-file-rejects-single-type`. |

### 21. `diagnostic-details-empty-supportedfixes-ca-rules`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `diagnostic-details-empty-supportedfixes-ca-rules` |
| Diagnosis | `diagnostic_details` returns `supportedFixes:[]` for CA-series rules (tested CA1826, CA1848) from `Microsoft.CodeAnalysis.NetAnalyzers`. Rules ship with code-fix providers but `CodeFixProviderRegistry` doesn't index them at workspace load. |
| Approach | In `src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs` (or equivalent), include `Microsoft.CodeAnalysis.CSharp.CodeStyle.dll` / `Microsoft.CodeAnalysis.CSharp.Features.dll` in the assembly scan when `NetAnalyzers` is referenced. Alternatively, document the gap in the tool description. |
| Scope | Production: 2 files (`CodeFixProviderRegistry.cs`, possibly `DiagnosticDetailsTools.cs` for response annotation). Tests: 1 fixture on a project referencing `NetAnalyzers`; assert `supportedFixes.Count > 0` for CA1826. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Adding assembly references may pull in unwanted fix providers; verify the scan stays bounded to `NetAnalyzers`-companion assemblies. |
| Validation | New fixture; existing diagnostic-details tests pass. |
| Performance review | One extra assembly scan at workspace load — measure latency impact. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `diagnostic_details` now indexes `Microsoft.CodeAnalysis.CSharp.CodeStyle` fix providers so CA-series rules surface non-empty `supportedFixes`. Closes gh #620. |
| Backlog sync | Close rows: `diagnostic-details-empty-supportedfixes-ca-rules`. |

### 22. `get-syntax-tree-range-truncates-at-statement`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-syntax-tree-range-truncates-at-statement` |
| Diagnosis | `get_syntax_tree(startLine, endLine)` returns only the first statement at `startLine` — sibling statements within the range are excluded. Range walker roots at the syntactic containing statement without collecting siblings at the same depth. Same tool as #11 (`get-syntax-tree-maxtotalbytes-not-enforced`) but different bug; do NOT bundle. |
| Approach | In `src/RoslynMcp.Roslyn/Services/SyntaxService.cs`, change the range-walker root selection: instead of "first statement at startLine", collect all top-level statements whose span overlaps `[startLine, endLine]`. |
| Scope | Production: 2 files (`SyntaxService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SyntaxTools.cs`). Tests: 1 fixture with method containing local-decl + try-catch on adjacent lines; assert both top-level statements appear. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Same file as #11 — schedule serially OR carefully merge if both initiatives land in the same wave. |
| Validation | New fixture; existing syntax-tree tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `get_syntax_tree(startLine, endLine)` now collects all top-level statements whose span overlaps the requested line range, not just the statement containing `startLine`. Closes gh #621. |
| Backlog sync | Close rows: `get-syntax-tree-range-truncates-at-statement`. |

### 23. `semantic-search-grep-pattern-broken`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `semantic-search-grep-pattern-broken` |
| Diagnosis | Two related bugs: (1) `semantic_search` falls back to token matching for all queries — no embedding match observed; (2) `semantic_grep` returns 0 matches for valid patterns. Pattern syntax undocumented. **This row has 2 distinct failure modes per Rule 1 anti-pattern — flag for refinement at intake; this batch ships the doc-and-fallback-truth fix only.** |
| Approach | Phase 1: document the fallback explicitly in the tool description (semantic_search → token-match if no embedding index); document `semantic_grep` pattern syntax (ripgrep regex). Phase 2 (separate row at intake): if embedding is intended to be active, fix the index path. |
| Scope | Production: 1 file (`src/RoslynMcp.Host.Stdio/Tools/SemanticSearchTools.cs` for description updates) + 1 file (`SemanticSearchService.cs` for `semantic_grep` doc / parameter fix). Tests: 1 fixture with a literal pattern guaranteed to match; assert match count > 0. **Rule 3 exemption: tool-surface-only, 2 files (description + parameter fix).** |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Phase 2 (embedding active) needs a separate row; not in this batch. |
| Validation | New fixture; manual smoke. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `semantic_search` and `semantic_grep` descriptions now document the active matching mode (token-fallback) and pattern syntax (ripgrep regex). Closes gh #627; embedding-mode fix split to a follow-up row at next intake. |
| Backlog sync | Close rows: `semantic-search-grep-pattern-broken`. Add new row for "semantic_search embedding index activation" at next intake. |

### 24. `validate-recent-git-changes-timeout`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `validate-recent-git-changes-timeout` |
| Diagnosis | Two symptoms: (1) 120s full-MCP timeout returns bare transport error (no `FailureEnvelope`); (2) Windows `git status` 10s sub-timeout silently falls back to full-workspace scope. **Row contains 2 distinct failure modes per Rule 1 anti-pattern; this batch ships symptom (1) — the correctness issue. Symptom (2) flagged for split at intake.** |
| Approach | In `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` (or `ValidationTools.cs`), wrap the 120s path in a try/catch that returns `FailureEnvelope(ErrorKind=Timeout, IsRetryable=true)` on timeout instead of bare transport error. Pattern already established for other tools. |
| Scope | Production: 2 files (`ValidationBundleTools.cs`, possibly `ValidationTools.cs`). Tests: 1 fixture simulating timeout; assert structured `FailureEnvelope`. **Rule 3 exemption: tool-surface-only, 2 files (envelope-shape change).** |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Pattern from `test_run` (already filed as gh #611) — same code shape. Verify the two implementations stay consistent. |
| Validation | New fixture; existing validate tests pass. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `validate_recent_git_changes` now returns structured `FailureEnvelope(ErrorKind=Timeout, IsRetryable=true)` on the 120s timeout path instead of a bare transport error. (Windows `git status` 10s sub-timeout fallback split to a follow-up row at next intake.) |
| Backlog sync | Close rows: `validate-recent-git-changes-timeout`. Add new row for "Windows git status 10s sub-timeout doc" at next intake. |

### 25. `find-overrides-interface-root-empty`

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-overrides-interface-root-empty` |
| Diagnosis | `find_overrides` on an interface member returns zero results despite `find_base_members` finding the base. Asymmetric navigation contract. |
| Approach | In `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (`find_overrides` implementation), when the target symbol is an interface member, descend through implementing types and collect their override of that member. Mirror the symmetric pattern from `find_base_members`. |
| Scope | Production: 2 files (`SymbolTools.cs`, possibly `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs` or equivalent override-resolution logic). Tests: 1 fixture covering interface member → `find_overrides` returns ≥1 result. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | Symbol-resolution code is delicate; verify the existing find-overrides tests for class hierarchies still pass. |
| Validation | New fixture; existing find-overrides tests pass. |
| Performance review | N/A — one additional symbol traversal. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed:** `find_overrides` on an interface member now returns the implementations (symmetric with `find_base_members`), closing the asymmetric-navigation gap. |
| Backlog sync | Close rows: `find-overrides-interface-root-empty`. |

---

## Self-vet (Step 7 checklist)

- All initiatives close 1 row each (no bundles).
- No initiative touches more than 4 production files. Tool-surface-only exemptions explicitly cited where claimed (initiatives #2, #4, #16, #18, #20, #23, #24).
- No initiative adds more than 3 test files (most add 1).
- No initiative's `estimatedContextTokens` exceeds 80K.
- Every initiative has a `toolPolicy` value (all `edit-only` — no `*_apply` calls needed in main-checkout; addenda forbids `*_apply` in main).
- Fanout estimates: only #1 (`apply-project-mutation-not-registered-revert`) was probed (fanout = 2, well under cap). Other initiatives don't mention rename/cross-cutting/attribute-change patterns.
- Hotspot distribution: initiatives #5 (`parallel-fanout-auto-reload-timeout-floor`) and #15 (`workspace-reloaded-during-call-conflates-notfound`) both touch `WorkspaceManager.cs` — distribute across waves (executor parallel-mode rule: ≤1 hotspot per wave).
- Initiatives #11 and #22 both touch `SyntaxService.cs` — distribute across waves OR serialize.
- Markdown link hrefs: all source citations use plain inline-code style. No bracket-paren markdown link syntax pointing at `src/` paths (would resolve relative to this plan dir and fail `verify-ai-docs.ps1`).

## Notes for `/backlog-sweep:review`

- Initiative #14 (`promote-tier-supports-prompts-and-resources`) is `deferred` from outset — design note Phase 1 only this batch. Reviewer should confirm the design-note-first sequencing is correct.
- Initiative #8 (`file-lock-aware-prompt-validation-guidance`) cites STALE anchors (the deleted `maintainer-overlay.md`) — flagged in Risks; executor must rewrite to canonical `full.md` path.
- Two pairs of initiatives touch the same file (`WorkspaceManager.cs`: #5+#15; `SyntaxService.cs`: #11+#22) — executor must schedule across waves, not adjacent.
