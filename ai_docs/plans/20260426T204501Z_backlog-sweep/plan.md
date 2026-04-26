# Backlog Sweep Plan - 2026-04-26

<!-- purpose: Initiative-level plan that groups related backlog rows into shippable PRs. -->
<!-- scope: in-repo -->

## Preamble

- Created at: 2026-04-26T20:45:01Z.
- Source backlog: `ai_docs/backlog.md` updated 2026-04-26T20:38:05Z.
- Scope: in-repo only. `ai_docs/planning_index.md` says unnamed planning work routes to `backlog.md` and does not open ecosystem scope.
- MCP availability: `.mcp.json` declares `roslyn` as stdio `roslynmcp`; live `server_info` reported `roslyn-mcp` 1.33.0 with registered surface parity OK. `RoslynMcp.slnx` loaded as workspace `0fec9f5c42c049d7b12c06ba5f6789ef` with 5 projects, 547 documents, and 0 workspace diagnostics.
- Anchor verification: completed for the active row. `symbol_search` resolved `PreviewStore`, `ToolDispatch`, and `ApplyByTokenAsync`; `document_symbols` resolved `ToolDispatch.ApplyByTokenAsync` at `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:86` and `:126`. Shell reads verified the hook matcher and report evidence.
- Candidate selection: one non-deferred row is shippable now, `pretooluse-apply-preview-token-invariant`. `workspace-process-pool-or-daemon` stays skipped because its `large-solution profile` dependency is not satisfied; `docs/large-solution-profiling-baseline.md:61` says the current small-sample run is not a substitute for 50+ project profiling, and `:72` says to repeat the template when a representative customer-scale solution is available.
- Bundling: none. There is only one active shippable row, so Rule 1 stays one row = one initiative.
- Planning-only sanity: this pass does not modify `ai_docs/backlog.md` or `CHANGELOG.md`. The executor closes backlog rows and writes changelog material in the implementation PR.

## Initiative 1

| Field | Content |
|-------|---------|
| **Initiative id** | `pretooluse-apply-preview-token-invariant` |
| **Status** | `pending` |
| **Backlog rows closed** | `pretooluse-apply-preview-token-invariant` |
| **Diagnosis** | The backlog row is still current. `hooks/hooks.json:14` protects many `mcp__roslyn__*_apply` tools with a PreToolUse prompt, and `hooks/hooks.json:18` asks the hook to infer valid preview evidence from recent conversation transcript. The 2026-04-26 retro records false blocks even after matching previews and `previewToken` inputs: `ai_docs/reports/20260426T162740Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md:253-254` and `:279-301`. The code already has deterministic token validation in the apply path: `ToolDispatch.ApplyByTokenAsync` resolves the workspace through `IPreviewStore.PeekWorkspaceId` at `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:86-92`, and the delegate overload throws when `peekWorkspaceId(previewToken)` returns null at `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:126-140`. `PreviewStore` records bounded token lifetime and revalidation state at `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:87-112` and retrieves/invalidate tokens at `:155-168`. A directory scan of `src/RoslynMcp.Host.Stdio/Tools` confirms the hook-protected apply tools already expose `string previewToken` and call `ToolDispatch.ApplyByTokenAsync` (examples: `RefactoringTools.cs:41-50`, `FileOperationTools.cs:48-58`, `OrchestrationTools.cs:94-104`, `ProjectMutationTools.cs:228-238`). Root cause: the shipped hook duplicates safety already enforced by tool inputs and preview stores, but does so through brittle transcript inspection that can fail when the hook cannot read enough conversation context. |
| **Approach** | Remove the transcript-scanning PreToolUse apply hook from `hooks/hooks.json` and rely on the apply tools' required `previewToken` parameters plus `ToolDispatch.ApplyByTokenAsync` / preview-store validation. Keep the Edit/Write release-managed guard and the PostToolUse verification reminder. Add a focused hook configuration regression test, preferably `tests/RoslynMcp.Tests/HookConfigurationTests.cs`, that parses `hooks/hooks.json` and asserts: (1) there is no PreToolUse prompt matcher for Roslyn apply tools that asks for transcript-based preview evidence; (2) the release-managed Edit/Write guard remains present; (3) the PostToolUse Roslyn apply verification reminder remains present. Do not replace the removed prompt with another prompt hook unless implementation finds an apply tool in the removed matcher that does not require `previewToken`; in that case split that tool-family gap into a follow-up backlog row instead of widening this initiative. Because `hooks/hooks.json` is release-managed, the executor must explicitly acknowledge that policy before editing it. |
| **Scope** | Production/config files touched: 1 - `hooks/hooks.json`. Test files added/modified: 1 - `tests/RoslynMcp.Tests/HookConfigurationTests.cs`. Files deleted: 0. Backlog/changelog sync files are expected in the execution PR but excluded from the Rule 3 count. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 tokens |
| **Risks** | Removing the hook shifts all preview enforcement to tool schemas and preview stores; verify every removed matcher entry either already requires `previewToken` or is intentionally left to a separate tool-level contract. Avoid weakening the release-managed file guard in the same JSON file. The hook file is shipped plugin surface, so validate JSON shape and plugin packaging checks. |
| **Validation** | Run `compile_check` after the test is added. Run targeted tests for `HookConfigurationTests` and `ToolDispatchTests`, then `./eng/verify-ai-docs.ps1` and `./eng/verify-release.ps1 -NoCoverage` before merge handoff. Manual reproduction target: after plugin reload, a preview/apply pair with a valid `previewToken` should reach the tool and fail only through tool-level token validation if the token is stale or invalid, not through transcript-grep hook denial. |
| **Performance review** | N/A - hook/config correctness fix, no hot-path service changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | **Roslyn apply hooks:** Removed transcript-based preview-evidence gating for Roslyn apply tools now that `previewToken` validation is enforced by tool inputs, `ToolDispatch.ApplyByTokenAsync`, and preview stores. (`pretooluse-apply-preview-token-invariant`; `hooks/hooks.json`; `ToolDispatch.ApplyByTokenAsync`) |
| **Backlog sync** | Close rows: `pretooluse-apply-preview-token-invariant`. Mark obsolete: none. Update related: leave `workspace-process-pool-or-daemon` deferred until large-solution profiling evidence exists. |

## Self-Vet

- [x] No initiative closes more than one row.
- [x] Production/config file count is 1, within Rule 3.
- [x] Test file count is 1, within Rule 4.
- [x] `estimatedContextTokens` is 25000, within Rule 5.
- [x] `toolPolicy` is explicit: `edit-only`.
- [x] Anchors in Diagnosis and Validation were verified with Roslyn read tools or direct source/report reads.
- [x] One correctness-flavored row is in the top 5 because it is the only pending initiative.
- [x] Total initiative count is honest for the current backlog: 1 shippable initiative plus 1 deferred dependency-gated row.

## Final Todo

- backlog: sync ai_docs/backlog.md
