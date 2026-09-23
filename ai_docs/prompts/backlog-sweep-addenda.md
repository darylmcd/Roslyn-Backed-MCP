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
  ./eng/verify-actionlint.ps1
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

- `ci_equivalent` mirrors the PR leg of `.github/workflows/ci.yml`, in order.
- `verify-release.ps1` alone is **not** the gate: `verify-changed-format` and `verify-nuget-audit` fail PRs a release build passes, and `verify-ai-docs` runs before the SDK is set up.
- CI shards `verify-release.ps1` across matrix legs; locally, run it unsharded.
- Skip steps only when context-tight; `fallback_compile` + targeted `mcp__roslyn__test_run` is the documented minimum substitute.
- Code-PR vs docs-only-PR topology (leg matrix, which gates are skipped, fail-closed classification, `validate-gate` / `validate` naming): [CI_POLICY.md](../../CI_POLICY.md) and `eng/resolve-ci-topology.ps1`. `ci_equivalent` is the code-PR shape; running it verbatim on a docs-only PR only over-validates.
- A row that touches a skill, agent, prompt, or `CHANGELOG.md` is a **code** PR however markdown-shaped it looks.
- `verify-changelog-fragments.ps1` runs standalone on docs-only PRs and as a `verify-release.ps1` child step on code PRs; running it explicitly first (as `ci_equivalent` does) is correct on both.
- **One-command local equivalent: `just ci`.** Its measured cost, timeout/background mode, hook runtime, exact filter, regeneration companions, flake registry, and `parallelSafe` value live in [AGENTS.md § Validation runtime](../../AGENTS.md#validation-runtime). Keep the machine-readable `ci_equivalent` list for consumers that must run or skip individual gates; `just ci` passes `-NoCoverage -ExcludeNetworkTests` through `verify-release-pr`.
- **Required check, no skip token.** The ruleset requires one status context, `validate`, produced by `validate-gate` on `pull_request` events. A `[skip ci]` token in any commit subject leaves it never-reported and the PR permanently BLOCKED, so `skipCiToken` is **empty**: never put a skip token in a commit on a PR branch here.
- `dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on `tests/RoslynMcp.Tests/bin/{Debug,Release}/net10.0/`. The parent `/ship` owner invokes it when its cleanup needs it; this addendum must never prescribe branch or worktree deletion. Check its exit status, not its stdout.

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

Faster and structurally accurate vs textual matching.

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

The full aggregate's operator-facing concurrency value is repeated in [AGENTS.md § Validation runtime](../../AGENTS.md#validation-runtime); the block below remains the parser-owned source for scoped-test parallelism and aggregate serialization.

```yaml
parallel_safety:
  parallelSafe: true
  serializeFullCi: true
  rationale: |
    Scoped targeted test runs are parallel-safe because test artifacts are
    isolated per test-assembly PROCESS. Every temp path is built under `TestTempRoot.Current`
    (`tests/RoslynMcp.Tests/TestInfrastructure/TestTempRoot.cs`) =
    `%TEMP%/RoslynMcpTests/run-<pid>-<rand>/`, and `[AssemblyCleanup]` deletes only
    that subtree — never the shared parent. Full `just ci` / `ci_equivalent`
    runs remain serialized: they share resource-heavy build/test infrastructure,
    and concurrent full gates produced host-contention failures in the 2026-09-20
    remediation run.
  evidence: |
    Two concurrent `dotnet test` invocations over the fixture-heavy undo/edit/
    project-mutation set: 74 passed each, 0 DirectoryNotFoundException
    (2026-08-10). The same command before the isolation landed produced 6
    failures. Row: test-temp-root-shared-cleanup-race.
  scope_caveat: |
    The evidence covers targeted tests on the fixture-copy path; it is not evidence
    for concurrent full gates. Keep `serializeFullCi: true`. Flip `parallelSafe`
    to false as well if scoped tests gain a machine-global dependency such as a
    fixed port, shared database, HKCU/%AppData% write, or a path outside
    `TestTempRoot.Current`.
```

**Regression guard:** `tests/RoslynMcp.Tests/TestTempRootTests.cs` asserts the load-bearing property — the abandoned-run reaper never deletes a *live* sibling run's directory, and never deletes the shared parent. A new temp path must combine against `TestTempRoot.Current`; re-deriving `Path.GetTempPath()` + `"RoslynMcpTests"` at a call site puts that path outside the isolation and re-opens the race.

## Hotspot files (parallel-mode wave rule: ≤1 per wave)

These files are touched by many initiatives by structural inevitability. The global executor's parallel-mode picker enforces ≤ 1 hotspot-touching initiative per wave. Re-measure citation pressure with `rg -l <file> ai_docs/items` when refreshing this section.

| File | Why it's a hotspot |
|---|---|
| `README.md` | **The repo's single biggest collision surface.** Its "**N tools** (X stable / Y experimental)" surface-count paragraph and the stable-only "currently N callable tools" sentence move on every tool add, rename, or tier promotion. Two initiatives editing it in one wave conflict on the same lines. |
| `src/RoslynMcp.Host.Stdio/README.md` | **Every tool-surface row edits it.** `HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog` asserts the identical surface-count paragraph. Rows historically forgot to cite it, but its edit frequency equals `README.md`'s; treat it as a same-wave collision surface with `README.md`. |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs` | Largest catalog partial. Every refactoring-tool registration/description/tier change lands here. |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs` | Same shape, orchestration surface. |
| `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs` | Wrapper file paired with the Refactoring partial; `[McpToolMetadata]` tier/name strings live here and must agree with the partial (RMCP001/RMCP002). |
| `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` | The Roslyn-service DI file, touched by every new Roslyn service. Not `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` (host-local plumbing only); there is no `src/RoslynMcp.Host.Stdio/Extensions/` directory. |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | Carries the whole `parameter_object_preview` pipeline (target validation, call-site binding, DTO emission, rewrite). Rows against it form a complete conflict graph: plan them as their own sequential conflict generations; do NOT expect a parallel wave. |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | Shared workspace state. Rows that share state without sharing a code path still do NOT bundle (Rule 1) and should not parallel-execute against this file in the same wave. |

**Heavily cited but NOT edited — do not schedule around these.** Both are *gates whose expectations are derived live*, so rows citing them never edit them:

| File | Reality |
|---|---|
| `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs` | Cited as the RMCP001/RMCP002 enforcement anchor; almost no row targets the analyzer itself. |
| `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs` | Asserts against `ServerSurfaceCatalog.Tools` **at run time** — a tool add or tier promotion moves no literal inside it. |

Citing a gate is correct and useful; scheduling waves around it, or charging it a Rule 4 test slot, is not. Neither file is a merge-collision surface, because nothing writes to it.

**The catalog partials are separate files.** `ServerSurfaceCatalog.{Refactoring,Orchestration,Editing,Symbols,Workspace,Analysis,Resources,Prompts}.cs` were split so unrelated tool areas stop colliding.

- Two initiatives touching *different* partials do not conflict: apply the ≤1-per-wave rule **per partial**, not to the catalog family as a whole.
- `ServerSurfaceCatalog.cs` itself (the shared base) is the exception: treat it as one hotspot.

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
  ---\ncategory: <Fixed|Changed|Changed — BREAKING|Added|Maintenance>\n---
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
| Registration | matching `ServerSurfaceCatalog.{Area}.cs` partial entry — the attribute and the partial row MUST agree or RMCP001/RMCP002 fail the build (`analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs`) — plus the DI line in `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` |

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

`ReadmeSurfaceCountTests` gates surface counts in **two** documents; a tool add moves `N` plus its tier's counter, a tier promotion moves `X` and `Y`:

- `RootReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog` — the "**N tools** (X stable / Y experimental)" paragraph in `README.md`.
- `HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog` — the identical paragraph in `src/RoslynMcp.Host.Stdio/README.md`.
- Both paragraphs must move together or the second test fails.

**`ReadmeSurfaceCountTests.cs` is deliberately NOT a companion.** It derives every expectation from the live catalog, so a tool add or tier promotion changes nothing inside it (see the *cited but not edited* table above). Listing it charged every tool-surface row a phantom Rule 4 test slot while hiding the real second companion.

**The stable-only callable count is gated too.** `RootReadmeStableCallableToolCount_MatchesStableOnlyRegistrationSurface` requires exactly one "currently N callable tools" claim in `README.md` and compares it with the live stable-only registration surface. A tier change or tool add that alters the stable-only count fails it, so update that sentence with the surface-count paragraph.

Further assertions in the same test class fire on a different trigger:

- `ReadmePackageAndPluginSkillCounts_MatchShippedSkillDirectory` fires on an edit under `skills/`: the "N bundled agent skills" claim in `README.md`, `src/RoslynMcp.Host.Stdio/README.md`, **and** `.claude-plugin/plugin.json`'s description must match the shipped `skills/` directory. `.claude-plugin/plugin.json` is a **hard-blocked release-managed path** (see Hooks): a shipped-skill add/remove routes through `/bump`, `/release-cut`, or `/ship`, or creates the sentinel explicitly.
- `BacklogPlanningSurfaceCountClaims_MatchLiveServerSurfaceCatalog` sweeps every planning/backlog `ai_docs/**/*.md` for "`<N>` tools is approaching" and `server_info.surface.registered.tools <from> -> <to>` claims, so a stale tool count in a backlog item or in this file fails the build too.

**Known over-trigger.** Description-, envelope-, and parameter-text-only edits touch those anchors without moving any count. Such rows are the `tool_surface_only` shape below: cite that exemption and drop both companions from the stanza, with the reason stated in Scope. Over-triggering is the deliberate default; the failure mode it prevents is a row that under-counts the gate and blows its Rule 3 budget at validation time.

## Tool-surface-only exemption (Rule 3)

Initiatives that ONLY change response-shape, error envelope, description text, or parameter defaults on an already-registered tool may touch up to **2 files**:

| File | Purpose |
|---|---|
| `src/RoslynMcp.Host.Stdio/Tools/{Tool}Tools.cs` | Wrapper edit — envelope, schema, description. |
| `src/RoslynMcp.Core/Models/{Tool}ResponseDto.cs` (optional) | DTO field add/rename. |

`toolPolicy` MUST be `"edit-only"`. Rule 4 still applies. Cite in Scope: *"Rule 3 exemption: tool-surface-only, 2 files."*

The 3-layer pattern would over-spec envelope/error-wrapper fixes; the 2-file cap captures them honestly.

## Hooks that block subagent tool calls

There is **no** `PreToolUse` gate on `mcp__roslyn__*_apply` in this repo; `toolPolicy` here is driven by the self-edit caveat below, not by a hook. What exists:

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

---

## Maintenance

- Update `Build / validation commands` when `.github/workflows/ci.yml`'s PR leg changes which `eng/*.ps1` scripts it runs — the addenda mirrors the workflow, not `verify-release.ps1` alone.
- Update `Hotspot files` when a partial split or refactor changes the parallel-merge friction surface. Do not record citation counts; they go stale immediately.
- Update `Structural-unit shape` if a new layer (e.g. `RoslynMcp.Host.Http`) is added.
- Update `Hooks that block subagent tool calls` against `.claude/settings.json` **and** `hooks/hooks.json` whenever either changes. Remove a hook's addenda entry in the same PR that removes the hook.
- Keep `## mandatory_companion_files` as a top-level `##` heading with the key inside a fenced block. `backlog.mjs audit` matches the heading literally (`mandatoryCompanionSection()` in `~/.claude/scripts/_anchor-classify.mjs`); demoting it to `###` or converting it back to a prose table silently disables mechanical companion expansion.
- Re-derive a companion entry from the gate's *source*, not from what rows habitually cite. A gate that asserts against live state (`ReadmeSurfaceCountTests` reading `ServerSurfaceCatalog.Tools`) is never a companion; only a file carrying a literal that must move is. Check with `git log --name-only` over the last ~20 commits touching the trigger anchor: a file no such commit edits is not a companion.
- Re-verify the tool-name prefix aliases whenever the install shape changes. Keep the hooks' exact dual-prefix matcher contract, `.claude/settings.json` allowlist parity, and the read-side primer's live-prefix resolution guidance synchronized.

Keep this file to repo facts: when a section becomes explanation rather than a parser-consumed fact, move it to a dedicated `ai_docs/` topic and link from here.
