# Backlog-remediation addenda — Roslyn-Backed-MCP

<!-- purpose: Repo-specific facts consumed by the global /backlog-remediate workflow. -->
<!-- scope: in-repo -->
<!-- contract: historical filename retained for compatibility; read by /backlog-remediate when present. -->

This file is the single source of repo-specific extensions to `/backlog-remediate`. The global workflow handles routing, state schema, plan-directory convention, ship discipline, and mode dispatch. Everything below is **facts about this repo** the workflow needs to do its job here.

If you change a fact (e.g. add a new hotspot file, swap build commands, ship a new analyzer that gates a structural unit), update this file in the same PR.

---

## Build / validation commands

```yaml
ci_equivalent: |
  ./eng/verify-changelog-fragments.ps1
  ./eng/verify-ai-docs.ps1
  ./eng/verify-release.ps1 -Configuration Release
  ./eng/verify-changed-format.ps1 -BaseRef origin/main -NoRestore
  ./eng/verify-nuget-audit.ps1 -SolutionPath RoslynMcp.slnx
doc_check: ./eng/verify-ai-docs.ps1
per_edit_compile: mcp__roslyn__compile_check
per_edit_test: mcp__roslyn__test_run --filter "<test-class-or-namespace>"
fallback_compile: dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true
fallback_test: dotnet test --filter "<filter>"
worktreeLockRelease: dotnet build-server shutdown
skipCiToken: ""   # NONE — see CI gate note below
```

`ci_equivalent` mirrors the PR leg of `.github/workflows/ci.yml`, in order. `verify-release.ps1` alone is **not** the gate: `verify-changed-format` (changed-file formatter findings) and `verify-nuget-audit` fail PRs that a release build passes, and `verify-ai-docs` runs *before* the SDK is even set up. CI shards `verify-release.ps1` across matrix legs (`-NoCoverage -ExcludeNetworkTests` plus `-TestShardIndex/-TestShardCount`, and `-TestShardOnly` on every non-artifact-owner leg); locally, run it unsharded. Skip steps only when context-tight; `fallback_compile` + targeted `mcp__roslyn__test_run` is the documented minimum substitute.

**`verify-changelog-fragments.ps1` is doubly covered — do not read the standalone CI step as the only run.** On a *code* PR the standalone step is skipped (`ci.yml:111` gates it on `docs_only == 'true'`); the fragment check still executes as a `verify-release.ps1` child step (`eng/verify-release.ps1:296-299`, inside the `-not $TestShardOnly` block that only the artifact-owner leg reaches). On a *docs-only* PR every leg is forced `-TestShardOnly`, that whole child block is skipped, and the standalone step is the sole coverage. Running it explicitly first, as `ci_equivalent` does, is correct on both paths.

**One-command local equivalent: `just ci`** (`verify-docs verify-skills verify-changed-format verify-actionlint verify-release-pr vuln-audit`). It is close but not identical to `ci_equivalent`: `verify-release-pr` passes `-NoCoverage -ExcludeNetworkTests` (matching CI's PR lane, and faster than the bare `-Configuration Release` above), `verify-skills` is redundant with the `verify-release.ps1` child step, and **`verify-actionlint` has no CI counterpart at all** — no workflow in `.github/workflows/` invokes actionlint, so it is a local-only lint. Prefer `just ci` for a full local pass; keep `ci_equivalent`'s explicit list when you need to run or skip individual gates.

**Topology split — `ci_equivalent` is the code-PR shape.** The `route` job runs `eng/resolve-ci-topology.ps1`, which classifies a PR as **docs-only** when every changed path matches `^(.*\.md|ai_docs/.*\.json)$` *and* none matches the behavior-bearing carve-out (`CHANGELOG.md`, or anything under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, `.github/prompts/`). A partial or over-capped files-API enumeration fails closed to the full matrix.

| | Code PR | Docs-only PR |
|---|---|---|
| Matrix | 4 hosted Windows + 2 Linux shards | 2 Linux shards (`docs-linux-{1,2}-of-2`) |
| `verify-ai-docs` | runs (artifact-owner leg) | runs (artifact-owner leg) |
| `verify-changelog-fragments` (standalone step) | **skipped** — covered by the `verify-release.ps1` child step instead | runs (artifact-owner leg); sole coverage on this path |
| `verify-release.ps1` | runs; artifact-owner leg packages and runs the full policy-child block | **runs**, but every leg is forced `-TestShardOnly` — tests shard, and the entire child-script block is skipped (package-family parity, version drift, skills-generic, plugin-package allowlist, changelog fragments, breaking-version, registry readiness, third-party notices) |
| `verify-changed-format` | runs on the artifact-owner leg | skipped |
| `verify-nuget-audit` | runs on the artifact-owner leg | skipped |
| `sdk-floor (10.0.400)` | runs | skipped |

So a docs-only initiative running `ci_equivalent` verbatim executes two scripts CI will skip. That is the safe direction — over-validating locally never lets a red PR through — so run the full list by default and drop those two only when you have confirmed the PR is docs-only by the rule above. Never assume the reverse: a row that touches a skill, agent, prompt, or `CHANGELOG.md` is a **code** PR no matter how markdown-shaped it looks.

**Required check + no skip token.** The `Default Branch Ruleset` requires exactly one status context: `validate`. That context is produced by `ci.yml`'s **`validate-gate`** job (not the job keyed `validate`, whose legs report as `validate-leg (<name>)`), which fans in `route` + every `validate-leg` + `sdk_floor` and names itself `validate` only on `pull_request` events (`validate-informational` otherwise, so a dispatch run can never satisfy the gate). A `[skip ci]` token in a commit subject leaves that check never-reported, so the PR is permanently BLOCKED — including on no-code state-flip/reconcile commits. `skipCiToken` is therefore **empty**: never put a skip token in any commit on a PR branch here.

`dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/`. The parent `/ship` owner invokes it when its canonical cleanup needs it; this addendum must never prescribe branch or worktree deletion. Its informational stdout is not an error, so cleanup checks must use its exit status rather than treat output as failure.

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

**Tool-name prefix is session-dependent — resolve it, never hardcode it.** The `mcp__roslyn__*` names above are the bare-server form. When the server is loaded as the Claude Code *plugin* (the default here — `roslyn-mcp@roslyn-mcp-marketplace`), every tool is exposed as `mcp__plugin_roslyn-mcp_roslyn__<tool>` and the bare names do not resolve. `.claude/settings.json` carries both prefixes in its allowlist for the same reason. A subagent briefing that pastes `preferred_read_side_tools` verbatim must resolve the live prefix first (call each `*server_info` candidate until one returns a Roslyn-shaped response, then pin that prefix) rather than assuming either form.

```yaml
preferred_read_side_tools:
  - mcp__roslyn__find_references
  - mcp__roslyn__symbol_search
  - mcp__roslyn__document_symbols
  - mcp__roslyn__compile_check
  - mcp__roslyn__project_diagnostics
  - mcp__roslyn__test_related_files
```

## Parallel-execution safety

```yaml
parallel_safety:
  parallelSafe: true
  rationale: |
    Test artifacts are isolated per test-assembly PROCESS, so two concurrent
    `ci_equivalent` / `dotnet test` runs on one machine do not contend on shared
    filesystem state. Every temp path is built under `TestTempRoot.Current`
    (`tests/RoslynMcp.Tests/TestInfrastructure/TestTempRoot.cs`) =
    `%TEMP%/RoslynMcpTests/run-<pid>-<rand>/`, and `[AssemblyCleanup]` deletes only
    that subtree — never the shared parent.
  evidence: |
    Two concurrent `dotnet test` invocations over the fixture-heavy undo/edit/
    project-mutation set: 74 passed each, 0 DirectoryNotFoundException
    (2026-08-10). The same command before the isolation landed produced 6
    failures. Row: test-temp-root-shared-cleanup-race.
  scope_caveat: |
    The evidence covers the fixture-copy path, which is where the contention was.
    It is NOT a proof that every test in the suite is parallel-safe. Flip
    parallelSafe back to false (which forces `/backlog-remediate` serial mode
    and engages `bsweep-state.mjs ci-lock-acquire`) if a NEW machine-global
    dependency appears — a fixed port, a shared database, an HKCU/%AppData% write,
    or any test writing outside `TestTempRoot.Current`.
```

**Regression guard:** `tests/RoslynMcp.Tests/TestTempRootTests.cs` asserts the load-bearing property — the abandoned-run reaper never deletes a *live* sibling run's directory, and never deletes the shared parent. A new temp path must combine against `TestTempRoot.Current`; re-deriving `Path.GetTempPath()` + `"RoslynMcpTests"` at a call site puts that path outside the isolation and re-opens the race.

## Hotspot files (parallel-mode wave rule: ≤1 per wave)

These files are touched by many initiatives by structural inevitability. The global executor's parallel-mode picker enforces ≤ 1 hotspot-touching initiative per wave.

Citation counts below are open-row counts measured over `ai_docs/items/*.md` on 2026-09-15; re-measure when this section is refreshed.

| File | Open rows citing | Why it's a hotspot |
|---|---|---|
| `README.md` | 39 | **The repo's single biggest collision surface.** Its surface-count line (`README.md:239`) and the stable-only callable count (`README.md:186`) move on every tool add, rename, or tier promotion. Two initiatives editing it in one wave conflict on the same line. |
| `src/RoslynMcp.Host.Stdio/README.md` | 1 cited / **every tool-surface row edits it** | The second gated README — `HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog` asserts the identical paragraph at `src/RoslynMcp.Host.Stdio/README.md:88`. Citation count is near zero only because rows historically forgot it; the *edit* frequency equals `README.md`'s. Treat it as a same-wave collision surface with `README.md`. |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs` | 16 | Largest catalog partial. Every refactoring-tool registration/description/tier change lands here. |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs` | 9 | Same shape, orchestration surface. |
| `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs` | 6 | Wrapper file paired with the Refactoring partial; `[McpToolMetadata]` tier/name strings live here and must agree with the partial (RMCP001/RMCP002). |
| `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` | low | 88 service registrations — DI line touched by every new Roslyn service. **This is the DI file**, not `Host.Stdio/ServiceCollectionExtensions.cs` (14 registrations, host-local plumbing only). There is no `Host.Stdio/Extensions/` directory. |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | 5 | 1873 lines carrying the whole `parameter_object_preview` pipeline (target validation, call-site binding, DTO emission, rewrite). Sweep `20260818T211226Z` planned six rows against it and the conflict graph came back a complete K6 — zero parallelizable initiatives, six sequential PRs (#1263/#1265/#1267/#1269/#1271/#1273). Plan rows here as their own conflict generations; do NOT expect a parallel wave. |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 2 | 1558 lines of shared workspace state. Citation pressure has dropped, but rows that share state without sharing a code path still do NOT bundle (Rule 1) and should not parallel-execute against this file in the same wave. |

**Heavily cited but NOT edited — do not schedule around these.** Two files out-rank most of the table on raw citation count yet are never edited by the rows citing them, because both are *gates whose expectations are derived live*:

| File | Open rows citing | Reality |
|---|---|---|
| `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs` | 36 (34 of them co-cited with `ReadmeSurfaceCountTests.cs`) | Cited as the RMCP001/RMCP002 enforcement anchor. Only 2 rows actually target the analyzer itself. |
| `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs` | 34 | Asserts against `ServerSurfaceCatalog.Tools` **at run time** — a tool add or tier promotion moves no literal inside it. Verified: of the last 22 commits touching a `ServerSurfaceCatalog.*.cs` partial, **zero** edited this file, while every tool-add edited `README.md`. |

Citing a gate is correct and useful; scheduling waves around it, or charging it a Rule 4 test slot, is not. Neither file is a merge-collision surface, because nothing writes to it.

**The catalog partials are separate files.** `ServerSurfaceCatalog.{Refactoring,Orchestration,Editing,Symbols,Workspace,Analysis,Resources,Prompts}.cs` were split precisely so unrelated tool areas stop colliding. Two initiatives touching *different* partials do not conflict — apply the ≤1-per-wave rule **per partial**, not to the catalog family as a whole. `ServerSurfaceCatalog.cs` itself (the shared base, 3 citations) is the exception: treat it as one hotspot.

## Virtually-shared files (orchestrator-owned in parallel mode)

Subagents in parallel mode MUST NOT edit these. The orchestrator's reconcile PR carries them.

- `ai_docs/backlog.md` (handled by `/close-backlog-rows`)
- `CHANGELOG.md` (this repo uses fragment convention; see below — but if a subagent ever edits this directly, it's a discipline break)

`README.md` and `src/RoslynMcp.Host.Stdio/README.md` are **not** on this list despite being the top collision surfaces. They are gate-forced doc edits that must land in the *implementing* PR — `ReadmeSurfaceCountTests` fails the build otherwise. Handle them by wave scheduling (hotspot rule), not by orchestrator ownership.

## Changelog convention

```yaml
changelogConvention: fragment
fragment_path: changelog.d/<row-id>.md
fragment_skill: /draft-changelog-entry
fragment_format: |
  YAML frontmatter is mandatory — the file MUST open with
  ---\ncategory: <Added|Changed|Fixed|Removed|Maintenance>\n---
  followed by the body. eng/verify-changelog-fragments.ps1 (first step of the
  PR gate) rejects a bare body.
consumed_by: /bump (rolls fragments into CHANGELOG.md at version-bump time)
```

`CHANGELOG.md` is a **build artifact** — never edit directly outside of `/bump`, and the release-managed guard enforces that. Always emit a fragment per closed row.

## Structural-unit shape (Rule 3 exemption)

A new `[McpServerTool]` follows the Core+Roslyn+Host.Stdio three-layer pattern. Counts as **structural units, not files** under Rule 3 — capped at ≤ 4 units per initiative. The indivisible new-tool shape:

| Structural unit | Typical files |
|---|---|
| Core contract | `src/RoslynMcp.Core/Services/I{Tool}Service.cs` + `src/RoslynMcp.Core/Models/{Tool}Result.cs` (+ optional request DTO) |
| Roslyn implementation | `src/RoslynMcp.Roslyn/Services/{Tool}Service.cs` |
| Host.Stdio tool surface | `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs`, carrying the `[McpToolMetadata]` attribute (tier + name) |
| Registration | matching `ServerSurfaceCatalog.{Area}.cs` partial entry — the attribute and the partial row MUST agree or RMCP001/RMCP002 fail the build (`analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:72,87`) — plus the DI line in `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` |

Plans for new-tool initiatives MUST set `toolPolicy: "edit-only"` and cite the structural-unit exemption in Scope.

Test-fixture DI is a further consequence, **counted in the budget**, not a 5th structural unit: a new `I{Tool}Service` must be registered in `tests/RoslynMcp.Tests/TestBase.cs` (and in `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs` for fixtures that use the container instead of inheriting `TestBase`), or DI-resolving tests fail at resolution time. This one has no mechanical trigger — "a *new* service was introduced" is not expressible as an anchor path — so the planner must add it by judgment. A plan that genuinely needs > 4 structural units must still split.

## mandatory_companion_files

Counted **in** the initiative's budgets (unlike virtually-shared files, which are orchestrator-owned and excluded). `backlog.mjs audit` expands these mechanically from `trigger_anchors`; the plan and implementation reviewers warn when a stanza under-counts them. The heading above and the key inside the block are load-bearing — `mandatoryCompanionSection()` matches the literal `## mandatory_companion_files` heading, and a prose table is invisible to it.

Budget classification, as the audit actually computes it: this repo does not declare docs-as-production (no `docs-are-production` marker in `ai_docs/backlog.md`'s header), so **both** companions land in the **doc** bucket and neither consumes a Rule 3 production slot or a Rule 4 test slot. Both must still appear in the plan stanza's Scope — the edit is real work and a merge-collision risk regardless of which budget it charges.

```yaml
mandatory_companion_files:
  - path: README.md
    trigger_anchors: [src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog*.cs, src/RoslynMcp.Host.Stdio/Tools/*Tools.cs]
  - path: src/RoslynMcp.Host.Stdio/README.md
    trigger_anchors: [src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog*.cs, src/RoslynMcp.Host.Stdio/Tools/*Tools.cs]
```

The `ReadmeSurfaceCountTests` gate (PR #294) asserts that the "**N tools** (X stable / Y experimental)" paragraph matches `ServerSurfaceCatalog` in **two** documents — `README.md:239` (`RootReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog`) and `src/RoslynMcp.Host.Stdio/README.md:88` (`HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog`). A tool add moves `N` plus its tier's counter; a tier promotion moves `X` and `Y`. Both paragraphs must move together or the second test fails.

**`ReadmeSurfaceCountTests.cs` is deliberately NOT a companion.** It derives every expectation from the live catalog, so a tool add or tier promotion changes nothing inside it — see the *cited but not edited* table above for the 22-commit evidence. Listing it here charged every tool-surface row a phantom Rule 4 test slot while hiding the real second companion.

**The stable-only callable count at `README.md:186` is NOT gated.** `CountPattern` requires the bolded `**N tools**` form; the prose "currently 94 callable tools" matches nothing and no other test asserts it. Re-derive it by hand on any tier change — nothing will fail if you forget. (Tracked: `readme-stable-callable-count-ungated`.)

Two further assertions in the same test class fire on a different trigger — an edit under `skills/`, not the catalog: `ReadmePackageAndPluginSkillCounts_MatchShippedSkillDirectory` requires the "N bundled agent skills" claim in `README.md`, `src/RoslynMcp.Host.Stdio/README.md:70`, **and** `.claude-plugin/plugin.json`'s description to match the shipped `skills/` directory. `.claude-plugin/plugin.json` is a **hard-blocked release-managed path** (see Hooks), so a shipped-skill add/remove cannot be completed by a plain subagent — it must route through `/bump`, `/release-cut`, or `/ship`, or create the sentinel explicitly. And `BacklogPlanningSurfaceCountClaims_MatchLiveServerSurfaceCatalog` sweeps every planning/backlog `ai_docs/**/*.md` for "`<N>` tools is approaching" and `server_info.surface.registered.tools <from> -> <to>` claims, so a stale tool count in a backlog item or in this file fails the build too.

**Known over-trigger.** Description-, envelope-, and parameter-text-only edits touch those anchors without moving any count. Such rows are the `tool_surface_only` shape below: cite that exemption and drop both companions from the stanza, with the reason stated in Scope. Over-triggering is the deliberate default — the prior failure mode was rows that silently *under*-counted the gate and blew their Rule 3 budget at validation time (case study PR #323).

## Tool-surface-only exemption (Rule 3)

Initiatives that ONLY change response-shape, error envelope, description text, or parameter defaults on an already-registered tool may touch up to **2 files**:

| File | Purpose |
|---|---|
| `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs` | Wrapper edit — envelope, schema, description. |
| `src/RoslynMcp.Core/Models/{Tool}ResponseDto.cs` (optional) | DTO field add/rename. |

`toolPolicy` MUST be `"edit-only"`. Rule 4 still applies. Cite in Scope: *"Rule 3 exemption: tool-surface-only, 2 files."*

Session evidence: 11 of 29 P3 rows in the 2026-04-24 intake are envelope/error-wrapper fixes. The 3-layer pattern would over-spec them; the 2-file cap captures them honestly.

## Hooks that block subagent tool calls

There is **no** `PreToolUse` gate on `mcp__roslyn__*_apply` in this repo. The historical "apply requires same-conversation preview evidence" hook (and the PR #230 `apply_composite_preview` redemption widening) is gone; `toolPolicy` here is driven by the self-edit caveat below, not by a hook. What actually exists:

```yaml
hooks:
  - tool: Edit|Write|MultiEdit
    script: eng/guard-release-managed-files.ps1   # .claude/settings.json PreToolUse
    blocks: |
      HARD BLOCK (exit 2) on these exact repo-relative paths:
        Directory.Build.props, manifest.json, CHANGELOG.md,
        .claude-plugin/{plugin,marketplace,mcp,server}.json,
        eng/verify-version-drift.ps1, eng/verify-skills-are-generic.ps1,
        hooks/hooks.json, and any BannedSymbols.txt
      Anything under tests/ or fixtures/ is exempt.
    override: |
      A sentinel file .release-managed-edit-allowed whose mtime is within
      RELEASE_SENTINEL_TTL_SECONDS (default 1800s). /bump, /release-cut and
      /ship create and remove it automatically. A subagent that needs one of
      these paths must either route through those skills or create the
      sentinel explicitly — it cannot Edit its way past the guard.
      See ai_docs/workflow.md § Release-managed file guard.
  - tool: Edit|Write|MultiEdit
    script: eng/verify-skills-on-edit.ps1         # .claude/settings.json PostToolUse
    effect: |
      Runs eng/verify-skills-are-generic.ps1 on any edit under skills/ and
      exits with the verifier's code, so a non-generic shipped-skill edit
      fails in the same turn. Silent on every other path.
  - tool: mcp__(?:plugin_roslyn-mcp_)?roslyn__(rename_apply|extract_interface_apply|extract_type_apply|move_type_to_file_apply|bulk_replace_type_apply|remove_dead_code_apply|apply_composite_preview|move_file_apply|create_file_apply)
    script: hooks/hooks.json                       # PostToolUse, type "prompt"
    effect: |
      ADVISORY ONLY — never blocks. Nudges the agent to run compile_check /
      build_workspace once a run of back-to-back applies ends. Plans must not
      treat it as a gate.
  - tool: mcp__(?:plugin_roslyn-mcp_)?roslyn__server_info
    script: hooks/hooks.json                       # PostToolUse, type "prompt"
    effect: |
      ADVISORY ONLY. Surfaces update.updateAvailable as a /roslyn-mcp:update
      nudge. Noise in a sweep session; never a gate.
```

Both `hooks/hooks.json` matchers accept exactly the bare `mcp__roslyn__*` and
marketplace-plugin `mcp__plugin_roslyn-mcp_roslyn__*` prefixes. They remain
advisory-only prompts under either registration shape and must not widen to unrelated
MCP servers. `hooks/hooks.json` is a hard-blocked release-managed path, so an
initiative that intentionally edits it must first create the fresh
`.release-managed-edit-allowed` sentinel described above and remove it before commit.

Planning consequence: `CHANGELOG.md` is release-managed *as well as* virtually-shared, so a subagent editing it hits a hard block before the discipline break is ever noticed. The fragment convention below is the only sanctioned path.

## Self-edit caveat (this is the Roslyn MCP server editing itself)

```yaml
selfEditCaveat:
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

See [ai_docs/runtime.md § Write-side by session shape](../runtime.md#write-side-by-session-shape) for the full two-sub-case policy.

## Subagents available in this repo

```yaml
initiative_executor: .claude/agents/initiative-executor.md  # use for Step 7 spawn
pr_reconciler:       .claude/agents/pr-reconciler.md         # readiness-only; parent owns /ship --land=<pr>
backlog_anchor_auditor: .claude/agents/backlog-anchor-auditor.md  # pre-plan anchor scan
backlog_intake_extractor: .claude/agents/backlog-intake-extractor.md  # Phase 1 of /backlog-intake
```

When the global execute command's Step 7 subagent briefing mentions "if available", these are what's available. The reconciler only reports readiness. The parent invokes `/ship --land=<pr>` and must preserve `SHIP_STATUS=landed-cleanup-failed` whenever landing succeeds but any local or remote cleanup proof fails; that status is a remediation signal, not a successful clean landing.

## Skills wired to backlog-remediation workflow

```yaml
draft_changelog_entry: /draft-changelog-entry
close_backlog_rows: /close-backlog-rows
reconcile_backlog_sweep_plan: /reconcile-backlog-sweep-plan
reconcile_backlog_vs_issues: /reconcile-backlog-vs-issues
recover_stalled_subagent: /recover-stalled-subagent
ship: /ship
```

These are the preferred-path skills the global commands reference. All resolve; the first five are repo-local under `.claude/skills/`, `/ship` is global under `~/.claude/skills/`.

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

- Update `Build / validation commands` when `.github/workflows/ci.yml`'s PR leg changes which `eng/*.ps1` scripts it runs — the addenda mirrors the workflow, not `verify-release.ps1` alone.
- Update `Hotspot files` when a partial split or refactor changes the parallel-merge friction surface. Re-measure the citation counts (`grep -rl <file> ai_docs/items/*.md`) and restamp the measurement date rather than carrying old numbers forward.
- Update `Structural-unit shape` if a new layer (e.g. `RoslynMcp.Host.Http`) is added.
- Update `Hooks that block subagent tool calls` against `.claude/settings.json` **and** `hooks/hooks.json` whenever either changes. Removing a hook without removing its addenda entry is the failure this section has already made once.
- Keep `## mandatory_companion_files` as a top-level `##` heading with the key inside a fenced block. `backlog.mjs audit` matches the heading literally (`_anchor-classify.mjs:442`); demoting it to `###` or converting it back to a prose table silently disables mechanical companion expansion.
- Re-derive a companion entry from the gate's *source*, not from what rows habitually cite. A gate that asserts against live state (`ReadmeSurfaceCountTests` reading `ServerSurfaceCatalog.Tools`) is never a companion; only a file carrying a literal that must move is. Check with `git log --name-only` over the last ~20 commits touching the trigger anchor: a file no such commit edits is not a companion.
- Re-verify the tool-name prefix aliases whenever the install shape changes. Keep the hooks' exact dual-prefix matcher contract, `.claude/settings.json` allowlist parity, and the read-side primer's live-prefix resolution guidance synchronized.
- Append to `Case studies` whenever a sweep retro produces a quotable new lesson — keep this section append-only; old entries are evidence.

The addenda file should hover around 150–250 lines. If it grows past 400, it's becoming a second planner — extract overflow to dedicated `ai_docs/` topics and link from here.
