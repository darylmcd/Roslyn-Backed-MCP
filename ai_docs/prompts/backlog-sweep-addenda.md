# Backlog-sweep addenda — Roslyn-Backed-MCP

<!-- purpose: Repo-specific facts consumed by the global /backlog-sweep:{plan,review,execute} commands. -->
<!-- scope: in-repo -->
<!-- contract: read by ~/.claude/commands/backlog-sweep/*.md when present in this repo. -->

This file is the single source of repo-specific extensions to the generic `/backlog-sweep` workflow. The global commands handle Rules 1–5, state.json schema, plan-dir convention, ship discipline, and mode dispatch. Everything below is **facts about this repo** the global commands need to do their job here.

If you change a fact (e.g. add a new hotspot file, swap build commands, ship a new analyzer that gates a structural unit), update this file in the same PR.

---

## Build / validation commands

```yaml
ci_equivalent: ./eng/verify-release.ps1 -Configuration Release
doc_check: ./eng/verify-ai-docs.ps1
per_edit_compile: mcp__roslyn__compile_check
per_edit_test: mcp__roslyn__test_run --filter "<test-class-or-namespace>"
fallback_compile: dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true
fallback_test: dotnet test --filter "<filter>"
worktree_lock_release: dotnet build-server shutdown
```

The two `verify-*.ps1` scripts are the authoritative CI gate. Skip them only when context-tight; the `fallback_compile` + targeted `mcp__roslyn__test_run` is the documented minimum substitute.

`dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/`. Always prepend before `git worktree remove --force` on Windows. Prints one informational line — not an error — so cleanup scripts must not treat non-zero stdout as failure.

## Read-side tool primer

The full pattern→tool table lives in [ai_docs/bootstrap-read-tool-primer.md](../bootstrap-read-tool-primer.md). Highest-leverage substitutions:

| Goal | Use this | Not this |
|---|---|---|
| Verify compile after edit | `mcp__roslyn__compile_check` | `dotnet build` |
| Run targeted tests | `mcp__roslyn__test_related_files` + `test_run --filter` | full `dotnet test` |
| Find callers / consumers | `mcp__roslyn__find_references` (with `metadataName` or `filePath+line+column`) | `Grep` for the simple name |
| Find symbol by name | `mcp__roslyn__symbol_search` | `Grep` |
| Enumerate file public surface | `mcp__roslyn__document_symbols` | `Grep public ` |
| Full-file diagnostics | `mcp__roslyn__project_diagnostics` | full build output parse |

5–30× faster and structurally accurate vs textual matching.

## Hotspot files (parallel-mode wave rule: ≤1 per wave)

These files are touched by many initiatives by structural inevitability. The global executor's parallel-mode picker enforces ≤ 1 hotspot-touching initiative per wave.

| File | Why it's a hotspot |
|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (and `*.Orchestration.cs`, `*.Refactoring.cs`, `*.Editing.cs`, `*.Symbols.cs`, `*.Workspace.cs`, `*.Analysis.cs`, `*.Resources.cs`, `*.Prompts.cs` partials) | Every new MCP tool registers here; many tool-extensions update descriptions. Catalog tracking gate enforced by the RMCP001/RMCP002 analyzers. |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | DI registration touched by every new service. |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 330+ lines of workspace state shared by many backlog rows; rows that share state but not code path do NOT bundle (see Rule 1) and should not parallel-execute against this file in the same wave. |

## Virtually-shared files (orchestrator-owned in parallel mode)

Subagents in parallel mode MUST NOT edit these. The orchestrator's reconcile PR carries them.

- `ai_docs/backlog.md` (handled by `/close-backlog-rows`)
- `CHANGELOG.md` (this repo uses fragment convention; see below — but if a subagent ever edits this directly, it's a discipline break)

## Changelog convention

```yaml
changelog_convention: fragment
fragment_path: changelog.d/<row-id>.md
fragment_skill: /draft-changelog-entry
consumed_by: /bump (rolls fragments into CHANGELOG.md at version-bump time)
```

`CHANGELOG.md` is a **build artifact** — never edit directly outside of `/bump`. Always emit a fragment per closed row.

## Structural-unit shape (Rule 3 exemption)

A new `[McpServerTool]` follows the Core+Roslyn+Host.Stdio three-layer pattern. Counts as **structural units, not files** under Rule 3 — capped at ≤ 4 units per initiative. The indivisible new-tool shape:

| Structural unit | Typical files |
|---|---|
| Core contract | `src/RoslynMcp.Core/Services/I{Tool}Service.cs` + `src/RoslynMcp.Core/Models/{Tool}Result.cs` (+ optional request DTO) |
| Roslyn implementation | `src/RoslynMcp.Roslyn/Services/{Tool}Service.cs` |
| Host.Stdio tool surface | `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs` |
| Registration | `ServerSurfaceCatalog.cs` partial entry (forced by RMCP001/RMCP002 analyzers) + `ServiceCollectionExtensions.cs` DI line |

Plans for new-tool initiatives MUST set `toolPolicy: "edit-only"` and cite the structural-unit exemption in Scope.

### Mandatory addenda (counted in file budget)

These are **counted in `productionFilesTouched`**, not exempt. They are mechanical consequences of the new-tool shape, not a 5th structural unit. A plan that genuinely needs > 4 structural units must still split.

| Addendum | File | Why |
|---|---|---|
| Test-fixture DI | `tests/RoslynMcp.Tests/TestBase.cs` | Register the new `I{Tool}Service` so fixture `ServiceProvider` can resolve it; tests requesting via DI fail at resolution time otherwise. |
| Test-fixture DI (container-style) | `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs` | Same, for fixtures using `TestServiceContainer` instead of inheriting `TestBase`. |
| README surface-count | `README.md` | The `ReadmeSurfaceCountTests` gate (PR #294) asserts `README.md`'s "N tools (X stable / Y experimental)" matches `ServerSurfaceCatalog`. New Experimental tool: `Y` and `N` +1. New Stable: `X` and `N` +1. |

## Tool-surface-only exemption (Rule 3)

Initiatives that ONLY change response-shape, error envelope, description text, or parameter defaults on an already-registered tool may touch up to **2 files**:

| File | Purpose |
|---|---|
| `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs` | Wrapper edit — envelope, schema, description. |
| `src/RoslynMcp.Core/Models/{Tool}ResponseDto.cs` (optional) | DTO field add/rename. |

`toolPolicy` MUST be `"edit-only"`. Rule 4 still applies. Cite in Scope: *"Rule 3 exemption: tool-surface-only, 2 files."*

Session evidence: 11 of 29 P3 rows in the 2026-04-24 intake are envelope/error-wrapper fixes. The 3-layer pattern would over-spec them; the 2-file cap captures them honestly.

## Hooks that block subagent tool calls

```yaml
preToolUse_blocks:
  - tool: mcp__roslyn__*_apply
    requires: prior in-conversation *_preview evidence
    cold_subagent_handling: |
      Cold-context subagents have zero prior turns and cannot point to a
      pre-existing preview. Two valid paths:
      (a) toolPolicy: "edit-only" — subagent uses Edit/Write only
      (b) toolPolicy: "preview-then-apply" — subagent calls the matching
          *_preview before each *_apply in the same turn sequence
      PR #230 widened the hook to also accept apply_composite_preview as
      redemption, but that does NOT help a cold subagent — it has no
      redemption either. Pick (a) or (b) per Rule 3b.
```

## Self-edit caveat (this is the Roslyn MCP server editing itself)

```yaml
self_edit:
  applies_when: working in the main checkout (NOT worktrees)
  forbidden_in_main: mcp__roslyn__*_apply, mcp__roslyn__*_preview
  reason: |
    The running MCP binary services tool calls against the MSBuildWorkspace
    snapshot it loaded at startup. *_apply mutates that snapshot, corrupting
    subsequent calls until workspace_reload.
  worktree_carveout: |
    Worktree sessions (.worktrees/<id>/) edit source while the MCP server
    being called is the installed global tool at
    %USERPROFILE%\.dotnet\tools\roslynmcp.exe — a distinct artifact NOT
    mutated by worktree-source edits. *_apply is safe and preferred in
    worktrees when the operation matches a refactor tool.
```

See [ai_docs/runtime.md § Bootstrap scope](../runtime.md#bootstrap-scope--self-edit-on-this-repository) for the full two-sub-case policy.

## Subagents available in this repo

```yaml
initiative_executor: .claude/agents/initiative-executor.md  # use for Step 7 spawn
pr_reconciler:       .claude/agents/pr-reconciler.md         # use for Step 10 merge+cleanup
backlog_anchor_auditor: .claude/agents/backlog-anchor-auditor.md  # pre-plan anchor scan
backlog_intake_extractor: .claude/agents/backlog-intake-extractor.md  # Phase 1 of /backlog-intake
```

When the global execute command's Step 7 or Step 10 mentions "if available", these are what's available.

## Skills wired to backlog-sweep workflow

```yaml
draft_changelog_entry: /draft-changelog-entry
close_backlog_rows: /close-backlog-rows
reconcile_backlog_sweep_plan: /reconcile-backlog-sweep-plan
recover_stalled_subagent: /recover-stalled-subagent
ship: /ship
```

These are the preferred-path skills the global commands reference. All present in this repo.

## Repo-specific overrides (none currently)

The global Rules 1–5 ceilings apply unmodified. Should this repo ever need to lower a cap (e.g. tighten Rule 3 from 4 to 3 files for a stretch), record it here as `overrides:` with rationale.

## Case studies (session evidence)

Compact pointer list — full retros live in `review-inbox/archive/<batch-ts>/` and the linked PR descriptions.

| Topic | PR / Date | Lesson |
|---|---|---|
| Three-layer Rule 3 under-estimate | PR #239 (2026-04-17) | New-tool initiatives ship 5–7 files; structural-unit exemption added in planner v3. |
| Catalog-touching parallel rebase | PRs #258, #260 (2026-04-18) | Two catalog-touching PRs in one wave forced second-to-merge into UNSTABLE → re-validate. ≤1 catalog-touching per wave. |
| README surface-count gate | PR #294 (2026-04) | Surface count assertion enforced — addenda counts README in file budget. |
| Compilation prewarm addenda missed | PR #323 (2026-04-22) | Plan missed the 3 mandatory addenda; all surfaced at validation time. Addenda list is now in this file. |
| WorkspaceManager hotspot family | PR #362 + 2026-04-22 retro | Multiple P3 rows citing `WorkspaceManager.cs` share state but not code path. Don't bundle; don't parallel-wave. |
| `heroic-last` → defer-at-closeout | 2026-04-22 orders 15/16 | Heroic rows often go obsolete by closeout. Prefer `status: "deferred"` from the outset if planner already suspects this. |

---

## Maintenance

- Update `Build / validation commands` if the verify scripts change names/paths.
- Update `Hotspot files` when a partial split or refactor changes the parallel-merge friction surface.
- Update `Structural-unit shape` if a new layer (e.g. `RoslynMcp.Host.Http`) is added.
- Update `Hooks that block subagent tool calls` if new PreToolUse hooks land.
- Append to `Case studies` whenever a sweep retro produces a quotable new lesson — keep this section append-only; old entries are evidence.

The addenda file should hover around 150–250 lines. If it grows past 400, it's becoming a second planner — extract overflow to dedicated `ai_docs/` topics and link from here.
