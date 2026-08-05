# Plan review — 2026-08-05 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260805T222513Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 pending
**Findings:** block: 0, warn: 5, info: 2
**Anchor verification:** performed

## Summary

No block findings. All ten initiatives sit inside Rule 3 (≤4 production files), Rule 4 (≤3 test files), and Rule 5 (≤80K); every one carries an explicit `toolPolicy: edit-only` (correct given the addenda's self-edit caveat forbidding `*_apply`/`*_preview` in the main checkout), and no `fanoutOversize` flags are set. I independently rebuilt the conflict graph from the Scope file lists and it matches the orchestrator's edge-for-edge — two edges, `(2,8)` on `WorkspaceValidationService.cs` and `(3,4)` on `CompileCheckTools.cs`. I byte-verified the first three initiatives' key anchors against current source (`TypeExtractionService.cs:56-64` refusal throw, `WorkspaceValidationService.cs:488-504` `ComputeOverallStatus` with the exact `|| errors.Any(d => d.Category=="Compiler")` disjunct, and `WorkspaceExecutionGate.cs:171-175`'s bare `KeyNotFoundException` precheck that initiative 3 correctly identified as the real failure shape beyond the row's own acceptance wording) — all three resolve exactly as described, and an existence sweep across all 23 cited production files plus 11 test files found only the three intentionally-new files missing. All ten `backlogRowsClosed` ids still exist in `ai_docs/backlog.md` — no stale rows.

The five warnings cluster on blast-radius honesty rather than budget. The sharpest is initiative 3: appending `IWorkspaceManager? workspaceManager = null` in trailing position to two `[McpServerTool]` methods is a public tool-schema change, and every one of the ~23 existing `IWorkspaceManager` tool parameters in `src/RoslynMcp.Host.Stdio/Tools/` is leading, non-nullable, and non-defaulted — the nullable-with-default DI shape the plan's opt-in-by-DI-availability design depends on has zero precedent in this codebase and is asserted, not verified. Initiative 10 records `fanoutEstimate: 1` while its own Risks field reports 14 referencing files enumerated, which defeats the point of the 5b reality check (I spot-verified the riskiest consumer, `EditService.cs:277-282`, already types its tuple as `IReadOnlyList<TextEditDto>`, so the compile-compat claim does hold at that anchor — hence warn, not block). Initiative 1 is the only initiative that skipped the fanout probe without explaining the skip in Risks, on an Approach that changes the exception type thrown and reasons about "every existing `catch (InvalidOperationException)`". Initiative 8 sits at the Rule 3 cap of 4 and then declares an "optional non-counted doc tag-along" in `ai_docs/runtime.md` — Rule 3's exclusion list covers tests, CHANGELOG, backlog.md, and plan/state.json only, so that self-granted exemption would make it 5. And initiatives 3 and 4 are adjacent-order with a hard conflict on the same method body of `CompileCheckTools.CompileCheck`.

Two advisories: initiative 7 ships with zero automated regression coverage (the plan correctly verified no Pester harness exists for `eng/*.ps1`, so this is a repo gap, not a planner miss), and initiative 9 is a deliberate partial slice of a size-L row that closes **zero** backlog rows — the skeleton `state.json` still lists `compilation-cache-adoption-read-side` in `backlogRowsClosed`, so the Phase C merge must clear it or the executor will wrongly close an open row. One scheduling note not rising to a finding: initiatives 2 and 8 both edit `WorkspaceValidationService.cs` with no `dependsOn` declared; the conflict edge is captured so wave-batching will serialize them, but 2 changes `ComputeOverallStatus`'s branch structure while 8 conditionally overrides its output — landing 2 first is the lower-friction order.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| extract-type-preview-refusal-missing-blocking-deps | warn | 5b | `fanoutEstimate: null` on an Approach that changes the exception type thrown at two sites and asserts "every existing `catch (InvalidOperationException)` ... keep matching unchanged"; Risks explains test-coverage grep but never explains the skipped fanout probe, unlike every sibling initiative that skipped it (planner Step 7 requires the skip be explained in Risks). |
| workspace-eviction-no-auto-retry-on-tool-call | warn | 5b | Blast radius omits the MCP tool-schema surface: appending `IWorkspaceManager? workspaceManager = null` to `compile_check` and `test_run` is a public-schema change, and all ~23 existing `IWorkspaceManager` tool params under `src/RoslynMcp.Host.Stdio/Tools/` are leading/non-nullable/non-defaulted — the nullable-with-default DI shape is unprecedented and unverified; if the SDK does not DI-resolve it the param leaks into the tool's JSON schema. |
| compile-check-multi-project-fallback-structured-scope | warn | C2-wave-conflict | Adjacent-order initiatives 3 and 4 share `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs` and both edit the SAME `CompileCheck` member (3 changes its signature + body, 4 rewrites its `[Description]`); planner Step 6 should have separated them. |
| validate-recent-git-changes-status-timeout-false-clean | warn | 3 | At the Rule 3 cap (4 production files) the Scope then declares an "optional non-counted doc tag-along" in `ai_docs/runtime.md`; Rule 3's exclusion list is tests/CHANGELOG/backlog.md/plan-state only, so that file is a 5th production file, not exempt. Drop it or split. |
| core-dto-fileeditsdto-array-to-readonlylist | warn | 5b | `fanoutEstimate: 1` contradicts the initiative's own Risks text ("14 total referencing files enumerated") — the recorded number is a files-I-will-edit count, not a fanout count. Reviewer spot-check of `EditService.cs:277-282` confirms the tuple is already `IReadOnlyList<TextEditDto>`, so the widening claim holds there; warn, not block. |
| stage-review-inbox-multisession-retro-glob-miss | info | 4 | `testFilesAdded: 0` — fix ships with no automated regression guard; plan correctly verified no `**/*.Tests.ps1` harness exists, so validation is a manual `-DryRun`. Advisory: a future glob regression will not be caught by CI. |
| compilation-cache-adoption-read-side | info | 1 | Deliberate partial slice of a size-L row: stanza declares `rowsClosed: []` and "leave the row open", but the skeleton `state.json` still carries `backlogRowsClosed: ["compilation-cache-adoption-read-side"]` and `rowsClosedCount: 1`. Phase C merge must clear both or the executor will close an open row. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [
    { "a": 2, "b": 8, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs"] },
    { "a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs"] }
  ],
  "degrees": { "1": 0, "2": 1, "3": 1, "4": 1, "5": 0, "6": 0, "7": 0, "8": 1, "9": 0, "10": 0 },
  "zeroDegreeInitiatives": [1, 5, 6, 7, 9, 10]
}
```

## Hotspot scheduling

Addenda hotspots: `ServerSurfaceCatalog.cs` (+ partials), `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`.

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none (3 explicitly excludes it; 9 reads `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` but edits no DI file) | n/a |

No hotspot-adjacency findings. Initiative 3 deserves credit for routing around `WorkspaceManager.cs` deliberately to stay under both the Rule 3 cap and the hotspot rule.

## Stale-row spot check

| Row id | Present? |
|---|---|
| extract-type-preview-refusal-missing-blocking-deps | yes (backlog.md:50) |
| validate-workspace-compiler-category-status-mismatch | yes (backlog.md:51) |
| workspace-eviction-no-auto-retry-on-tool-call | yes (backlog.md:52) |
| compile-check-multi-project-fallback-structured-scope | yes (backlog.md:62) |
| direct-mutation-undo-byte-fidelity | yes (backlog.md:64) |
| recommend-workflow-missing-semantic-grep-route | yes (backlog.md:65) |
| stage-review-inbox-multisession-retro-glob-miss | yes (backlog.md:66) |
| validate-recent-git-changes-status-timeout-false-clean | yes (backlog.md:67) |
| compilation-cache-adoption-read-side | yes (backlog.md:61) |
| core-dto-fileeditsdto-array-to-readonlylist | yes (backlog.md:91) |

## Recommended next step

Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the five warnings in the run summary. Highest-value pre-execute actions, in order: (1) drop initiative 8's `ai_docs/runtime.md` tag-along or accept it as a 5th file with an explicit override; (2) re-order or wave-separate initiatives 3 and 4 so the shared `CompileCheck` member is not edited in the same wave; (3) brief the executor on initiative 3 that the nullable-trailing DI parameter shape must be verified against the ModelContextProtocol SDK's parameter-binding rules before the retry design is assumed to be opt-in; (4) clear `backlogRowsClosed`/`rowsClosedCount` for initiative 9 during the Phase C state merge.