# Plan review — 2026-05-05T19:50:00Z

**Plan reviewed:** `ai_docs/plans/20260505T191730Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:review
**Outcome:** passed-with-warnings
**Initiative count:** 16
**Findings:** block: 0, warn: 2, info: 4
**Anchor verification:** performed (first 3 initiatives + key cross-cutting paths)

## Summary

Plan passes Rules 1, 3, 3b, 4, 5 across all 16 initiatives. Two warnings: (1) initiatives #10 and #11 both touch `WorkspaceManager.cs` at adjacent orders — the plan's self-vet incorrectly claimed non-adjacent, but the actual order positions are 10 and 11; the executor's parallel-mode wave-picker will refuse to pair them. Mitigated by #11's existing notes, which flag deferring/obsoleting #11 after #10 lands. (2) Initiative #2's `hotspotFiles` field and Approach text both cite a stale path (`src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs`) — the real file lives at `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` (no `/Extensions/` subdirectory). Same drift exists in `ai_docs/prompts/backlog-sweep-addenda.md`'s hotspot list, which is a separate fix. Four info findings document minor scope-citation noise that doesn't block execution.

## Findings

| Initiative | Severity | Rule / Topic | Evidence |
|---|---|---|---|
| `workspace-cache-prewarm-on-load` (#10), `workspace-warm-default-above-50-projects` (#11) | **warn** | hotspot-scheduling | Both touch `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` at adjacent orders 10/11. Plan's self-vet checklist claims `WorkspaceManager.cs touched by #4, #10, #11 — order positions 4, 10, 11; non-adjacent` but 10 and 11 are consecutive integers. Parallel-mode wave-picker (per addenda's `≤1 hotspot-touching per wave` rule) will refuse to pair them; serial scheduling required. #11's `notes` already suggest deferring/obsoleting after #10 — execute step should honor that. |
| `workspace-cache-store-infrastructure` (#2) | **warn** | anchor-freshness | `state.json.hotspotFiles` cites `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` which does not exist on disk. Real path: `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`. The plan.md Approach paragraph for #2 carries the same stale path. The addenda's hotspot list (`ai_docs/prompts/backlog-sweep-addenda.md` § Hotspot files) is the upstream source of the drift and should be fixed separately. Executor will encounter the wrong path during Step 4 and have to relocate; flagging here is cheaper. |
| `audit-deep-skill-create-and-mode-split` (#12) | info | rule-3-exemption | Cites `Rule 3 structural-unit exemption: 4 units (skill + 3 mode prompts)` in plan.md Scope, but `productionFilesTouched: 4` already fits Rule 3's standard cap (4). The exemption citation is unnecessary; no scope violation either way. Separate quibble: the addenda's structural-unit exemption is defined for new `[McpServerTool]` shape (Core + Roslyn + Host.Stdio + Registration), not for new skills — the plan's reuse of the term for a new skill is a stretch. Recommend the next addenda update either (a) define a "new skill" structural-unit shape explicitly, or (b) remove the exemption citation from #12's plan since it's not needed at the 4-file count. |
| `audit-deep-skill-create-and-mode-split` (#12), `audit-deep-archive-and-surface-audit-integration` (#13) | info | dual-close-pattern | Both initiatives' `backlogRowsClosed` list contains `audit-deep-skill-migration` (intentional, per heroic-bundle pre-split). The `/close-backlog-rows` skill refuses on zero matches. After the first PR closes the row, the second PR's close-rows step will refuse — the executor's Step 9 (serial-mode backlog sync) needs to no-op the close on the second initiative. State.json `notes` for #12 already documents this; the executor must honor it. Suggest either: (a) only #12's PR carries the close-rows action, #13's PR omits it; or (b) make `/close-backlog-rows` no-op-safe on already-closed ids in a separate small PR. |
| `tool-output-schema-batch-1-server-info-workspace` (#5) | info | rule-5-band-edge | `estimatedContextTokens: 60000` is the heaviest in the plan (vs 50K typical High band, 35–55K elsewhere). Within Rule 5 ceiling (80K). Plan's Risks paragraph already documents the split-fallback (server-only or workspace-only batches if 4-file cap proves tight). No action; flagging so the executor knows to bail to the documented split rather than push past 80K. |
| `tool-output-schema-infrastructure` (#3) | info | rule-3b-csproj-edit | Plan notes a possible 4th file: `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` package add for `JsonSchema.Net` (only if S.T.J built-in schema-gen proves insufficient). `toolPolicy: "edit-only"` covers .csproj XML edits — they're text edits, no destructive `*_apply` needed — but the resulting NuGet restore is a side-effect. No action; flagging the non-code-source edit boundary. |

## Anchor verification details

Verified the following paths via filesystem check:

| Path | Status |
|---|---|
| `src/RoslynMcp.Host.Stdio/Tools/ProgressHelper.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Catalog/McpToolMetadataAttribute.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs` | ✓ |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | ✓ |
| `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs` | ✓ |
| `src/RoslynMcp.Roslyn/Services/WorkspaceWarmService.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` | ✓ |
| `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` | ✓ |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | **STALE — see warn finding** |
| `src/RoslynMcp.Host.Stdio/AddRoslynMcpHostServices.cs` (cited in row #2 backlog text) | **STALE — file does not exist; the plan's `ServiceCollectionExtensions.cs` is the right host but the path prefix is wrong** |

Real DI registration files (verified via grep): `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` and `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`.

## Backlog row freshness

All 15 distinct row ids referenced by `backlogRowsClosed` are present in `ai_docs/backlog.md` (one row, `audit-deep-skill-migration`, is referenced by two initiatives — intentional heroic-split).

## Recommended next step

Outcome is **passed-with-warnings**. Proceed at user discretion:

- The hotspot-adjacency warning (#10/#11) is mitigated by #11's defer/obsolete notes — the executor's Step 6 (verify plan still accurate) will likely mark #11 obsolete after #10 lands. No re-plan needed.
- The stale-anchor warning on #2 is a doc-drift issue. Executor will discover the right path (`src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`) during Step 4 — prefer fixing the addenda's hotspot list (`ai_docs/prompts/backlog-sweep-addenda.md`) in a separate small PR before the next sweep so the drift doesn't recur.

If user is comfortable with the two warnings: run `/backlog-sweep:execute` (state.json `reviewStatus` is now `passed-with-warnings` — execute proceeds without override).

If user wants either warning addressed first:
- For the hotspot-adjacency: re-run `/backlog-sweep:plan plan-id=ai_docs/plans/20260505T191730Z_backlog-sweep/` to swap orders or mark #11 deferred from the outset.
- For the stale anchor: file a small PR to update the addenda's hotspot list and the plan's #2 anchors. (No re-plan required; just text fixes.)
