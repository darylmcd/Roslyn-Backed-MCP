# Runtime

<!-- purpose: Build/test/run commands, execution context, and Roslyn MCP client policy. -->

This document is the canonical runtime and execution-context reference for AI agents and maintainers.

## Execution Context

- Primary runtime target: local stdio host process.
- Workspace model: `MSBuildWorkspace` over on-disk files.
- Unsaved editor buffers are not authoritative for semantic operations.

## Task Runner

All commands below are available as `just` recipes. Run `just --list` for the full menu, or `just ci` to run the complete local CI gate.

| Recipe | What it does |
|--------|-------------|
| `just build` | Debug build |
| `just test` | Full test suite |
| `just validate` | Fast local check (build + test) |
| `just ci` | Mirrors CI pipeline (docs + release validation + vuln audit) |
| `just run` | Start the stdio host process |
| `just reinstall` | Full reinstall: Layer 1 (global tool) + Layer 2 (Claude Code plugin) |
| `just tool-install-local` | Layer 1 only: pack + install global tool from local build |
| `just plugin-reload` | Layer 2 only: reload Claude Code plugin from local repo |

See `justfile` in the repo root for the complete recipe list including packaging, Docker, and security audit recipes.

## Platform And Tooling

- .NET SDK: 10.0.100 (rollForward: latestFeature) — see `global.json`
- Primary v1 operating system target: Windows. Cross-platform (macOS, Linux) supported wherever .NET 10 SDK is available.
- Main local validation entry point: `just ci` (or `./eng/verify-release.ps1` directly)
- Fast manual commands:
  - `just build` / `dotnet build RoslynMcp.slnx --nologo`
  - `just test` / `dotnet test RoslynMcp.slnx --nologo`
  - `just run` / `dotnet run --project src/RoslynMcp.Host.Stdio`

## Package Identity

- **NuGet package ID:** `Darylmcd.RoslynMcp` (NOT `RoslynMcp` — that is a different publisher's package)
- **CLI command after install:** `roslynmcp`
- **Install:** `dotnet tool install -g Darylmcd.RoslynMcp`
- **Update:** `dotnet tool update -g Darylmcd.RoslynMcp`

The project name `RoslynMcp.Host.Stdio` is the .csproj assembly name, not the NuGet package ID. Always use `Darylmcd.RoslynMcp` in `dotnet tool` commands.

## Environment variables (stdio host)

Optional overrides read at startup from `src/RoslynMcp.Host.Stdio/Program.cs`. Values must be positive integers unless noted.

| Variable | Affects | Default (when unset) |
|----------|---------|----------------------|
| `ROSLYNMCP_MAX_WORKSPACES` | `WorkspaceManagerOptions.MaxConcurrentWorkspaces` | 8 |
| `ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS` | `WorkspaceManagerOptions.MaxSourceGeneratedDocuments` | 500 |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `ValidationServiceOptions.BuildTimeout` | 5 minutes |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `ValidationServiceOptions.TestTimeout` | 10 minutes |
| `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` | `ValidationServiceOptions.VulnerabilityScanTimeout` | 2 minutes |
| `ROSLYNMCP_MAX_RELATED_FILES` | `ValidationServiceOptions.MaxRelatedFiles` | 25 |
| `ROSLYNMCP_FAST_FAIL_FILE_LOCK` | When unset or `true`, `test_run` terminates the child `dotnet test` on the first MSB3027/MSB3021 line so the FailureEnvelope returns within ~200ms instead of ~10s. Set to `false` to restore pre-v1.17 behavior (wait for MSBuild's 10× retry loop). | `true` |
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `PreviewStoreOptions.MaxEntries` | 20 |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `PreviewStoreOptions.TtlMinutes` | 5 minutes |
| `ROSLYNMCP_PREVIEW_PERSIST_DIR` | `PreviewStoreOptions.PersistDirectory` — when set, composite preview tokens are written to disk so a separate `roslynmcp` process (e.g. an apply-phase sub-agent) can redeem them. Solution-backed previews stay in-memory only. | unset (in-memory only) |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `ExecutionGateOptions.RateLimitMaxRequests` | 120 |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `ExecutionGateOptions.RateLimitWindow` | 60 |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `ExecutionGateOptions.RequestTimeout` | 120 |
| `ROSLYNMCP_ON_STALE` | `ExecutionGateOptions.OnStale` — stale-workspace policy (`auto-reload`, `warn`, `off`) | `auto-reload` |
| `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` | `SecurityOptions.PathValidationFailOpen` | `false` (must parse as `true`/`false` to override) |
| `ROSLYNMCP_SCRIPT_MAX_CONCURRENT` | `ScriptingServiceOptions.MaxConcurrentEvaluations` | 4 (FLAG-5C: max in-flight `evaluate_csharp` calls racing their hard deadline) |
| `ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS` | `ScriptingServiceOptions.ConcurrencySlotAcquireTimeoutSeconds` | 5 seconds |
| `ROSLYNMCP_SCRIPT_MAX_ABANDONED` | `ScriptingServiceOptions.MaxAbandonedEvaluations` | 8 (FLAG-5C: abandoned-worker-thread cap; restart host when hit) |
| `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` | `ScriptingServiceOptions.TimeoutSeconds` | 10 seconds |
| `ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS` | `ScriptingServiceOptions.WatchdogGraceSeconds` | 10 seconds (hard-kill grace after the script budget expires) |

## Claude Code Plugin

The server is also distributed as a Claude Code plugin. Plugin-relevant configuration:

- `.mcp.json` passes user-configurable env vars via `${user_config.*}` placeholders to the `roslynmcp` process.
- `hooks/hooks.json` defines PreToolUse and PostToolUse hooks for safety enforcement.
- Plugin skills in `skills/` compose multiple MCP tools into guided workflows; they run as Claude Code skill prompts, not as MCP protocol extensions.

Install via: `/plugin marketplace add darylmcd/Roslyn-Backed-MCP` then `/plugin install roslyn-mcp@roslyn-mcp-marketplace`

## MCP Runtime Notes

- `stdout` is reserved for MCP protocol traffic.
- Operational logging should go to `stderr`.
- For custom stdio clients (not Cursor / Claude Code), see [`docs/stdio-client-integration.md`](../docs/stdio-client-integration.md) — covers NDJSON framing, init handshake, and minimal Python / C# client examples.

## Roslyn MCP client policy (AI sessions)

The Roslyn MCP server is the **default tool surface for all C# work in this repository**
— navigation, search, diagnostics, verification, AND structured edits. The policy is
three-part: read-side, write-side, and the bootstrap scope for self-edits.

### Read-side — prefer over Grep / Bash in every session (including bootstrap)

This is the most under-used quadrant. Every read tool listed below is safe under every
condition — including bootstrap self-edit on this repo — and is 5–30× faster than the
Bash / Grep alternative. The short answer is: **if your next action is "find / search /
locate / enumerate / verify / compile / test-targeted-subset", reach for the Roslyn MCP
tool first.**

Full pattern→tool mapping, session-verb triggers, and anti-pattern examples live in
[`ai_docs/bootstrap-read-tool-primer.md`](bootstrap-read-tool-primer.md). The primer is
intentionally short and greppable; bookmark it. The highest-leverage substitutions:

| Reach for… | Instead of… | Win |
|---|---|---|
| `compile_check` | `Bash: dotnet build` | 5–30× faster on loaded workspace; identical diagnostics |
| `test_related_files` + `test_run --filter` | `Bash: dotnet test --no-build` | ~2–5× faster; scoped to touched files |
| `find_references` / `find_consumers` / `find_implementations` | `Grep` for a symbol name | Symbol-identity exact; no same-simple-name false matches |
| `symbol_search` | `Grep` for a type / member name | Handles camelCase + FQN natively |
| `document_symbols` / `enclosing_symbol` | `Read` + eyeball | Semantic, not textual |

### Write-side — preview → apply on peer repos; bootstrap-restricted on this repo

When a Roslyn-backed refactor tool covers the edit (for example `rename_*`, `extract_*`,
`move_type_*`, `change_signature_*`, `code_fix_*`, `organize_usings_*`,
`bulk_replace_type_*`, `split_class_*`), use it on peer repositories in preference to
hand-editing multiple files or relying on text replacement. Use `*_preview` first,
review the diff, then call `*_apply` with the returned handle. Align with
[Session And Mutation Safety](#session-and-mutation-safety) (workspace id, version
checks).

### Bootstrap scope — self-edit on THIS repository

The self-edit bootstrap caveat restricts **write-side `*_apply` tools** when — and only
when — the binary servicing the MCP call is the same binary whose source tree is being
edited. There are two concrete shapes this takes in day-to-day work:

#### Main-checkout self-edit (restricted)

Running `dotnet run --project src/RoslynMcp.Host.Stdio` (or a freshly rebuilt
`roslynmcp` whose source IS the main checkout) against a workspace loaded from that
same checkout. Here `*_apply` is forbidden — the binary under edit is servicing the
call, so any apply mutates files underneath a stale `MSBuildWorkspace` snapshot.

- ❌ `*_apply` (rename_apply, extract_type_apply, create_file_apply, etc.) — use
  `Edit` / `Write`.
- ✅ `*_preview` — still useful for visualizing the diff before you hand-edit.
- ✅ **Every read-side tool** — see the primer.

#### Worktree self-edit (the `workflow.md` default — `*_apply` is safe)

Subagent sessions spawned under `backlog-sweep-execute.md` (and most other task work)
run inside a `.worktrees/<id>/` worktree against the **installed global tool** at
`%USERPROFILE%\.dotnet\tools\roslynmcp.exe` — the running binary is a distinct, already-
built artifact that is NOT mutated by edits to the worktree source tree. The binary-
under-edit rationale does not hold.

- ✅ `*_apply` — safe, and preferred when a refactoring tool covers the operation
  (rename, extract/move type, code fix, bulk type replace, organize usings, format).
- ✅ `*_preview` — use first; review the diff; then apply.
- ✅ **Every read-side tool** — same as above.

Discipline requirements for worktree `*_apply`:

1. Load the worktree's own solution — `workspace_load` on `.worktrees/<id>/RoslynMcp.slnx`,
   not the main-checkout path. Mixing them up applies edits to the wrong tree.
2. If a subsequent tool call depends on the post-apply state, call `workspace_reload`
   (or rely on the server's change tracker) to refresh the snapshot. This is the
   ordinary peer-repo discipline, not a bootstrap-specific constraint.

The canonical session-verb → tool table is in
[`bootstrap-read-tool-primer.md`](bootstrap-read-tool-primer.md) — agents should read
that file at session start alongside the other bootstrap docs.

### When the server is disconnected

Check `server_info` or try any `mcp__roslyn__*` call. If the server reports "not
connected", the Grep / Bash fallbacks in the primer's fallback column are appropriate.
Log the disconnect in the PR description and follow up with the
`mcp-connection-session-resilience` diagnostics.

For the full long-form decision tree across every tool (including the read-side list
above + all write-side tools + skill composites), see
[`domains/tool-usage-guide.md`](domains/tool-usage-guide.md).

## Self-edit recipe

This section is the operational home for **how to edit this repository's source**
when the Roslyn MCP server is the tool surface you're editing with. It complements
— and does not duplicate — the pattern→tool table in
[`bootstrap-read-tool-primer.md`](bootstrap-read-tool-primer.md): the primer answers
"which tool for this verb?", this recipe answers "what is the end-to-end loop?".

Four sessions (v1.15.0, v1.16.0, PRs #165–#178, PRs #182–#194) shipped without
reaching for read-side MCP tools even though the primer was in the bootstrap order.
The recipe below is the explicit shape that closes that gap.

### The three steps (worktree self-edit — the default)

Subagent sessions spawned under `ai_docs/prompts/backlog-sweep-execute.md` run inside
`.worktrees/<id>/` against the installed global tool at
`%USERPROFILE%\.dotnet\tools\roslynmcp.exe`. The binary-under-edit rationale does
not hold here — use every Roslyn MCP tool, read- AND write-side, as you would on a
peer repository.

1. **Load the worktree's own solution.** Call `workspace_load` with
   `.worktrees/<id>/RoslynMcp.slnx` (NOT the main-checkout path). Mixing paths
   applies edits to the wrong tree.
2. **Use read-side MCP tools for navigation and pre-edit exploration.** Every row
   in the primer's "Pattern → tool (read-side — always safe)" table is 5–30× faster
   than the Grep / Bash alternative. The highest-value substitutions:
   `find_references` for "who calls X", `symbol_search` for "find the type by
   name", `document_symbols` for "what's in this file", `compile_check` for
   "does this still compile".
3. **Apply edits with `Edit` / `Write` (main-checkout) or `*_apply` tools
   (worktree), then verify with `compile_check` + `test_run --filter`.** The
   verify loop is the whole reason the read-side tools exist — `compile_check`
   runs <1s on a loaded workspace; `Bash: dotnet build` takes 5–30s for the same
   diagnostics.

### Worked example — the full Edit → verify loop

Scenario: a small bug fix in `src/RoslynMcp.Roslyn/Services/SomeService.cs` that
touches three methods. Typical session shape in a worktree:

```text
1. workspace_load(path=".worktrees/self-edit-xyz/RoslynMcp.slnx")
   → workspaceId = "ws-abc"

2. find_references(workspaceId="ws-abc",
                   metadataName="RoslynMcp.Roslyn.Services.SomeService.DoTheThing")
   → 4 call sites, 2 in tests/, 2 in src/

3. symbol_info(workspaceId="ws-abc",
               filePath=".worktrees/self-edit-xyz/src/.../SomeService.cs",
               line=42, column=18)
   → confirms the symbol under the caret is what the fix targets

4. Edit(file_path=".worktrees/self-edit-xyz/src/.../SomeService.cs", ...)
   → hand edit (OR, for a pattern-match refactor, rename_preview → rename_apply)

5. workspace_reload(workspaceId="ws-abc")
   → refreshes the in-memory snapshot so downstream semantic calls see the edit

6. compile_check(workspaceId="ws-abc")
   → ~0.5s, structured diagnostics; if clean, continue

7. test_related_files(workspaceId="ws-abc",
                      filePaths=[".worktrees/.../SomeService.cs"])
   → returns a --filter string covering the tests that exercise the touched file

8. test_run(workspaceId="ws-abc", filter="<the --filter string from step 7>")
   → ~2–5s scoped run, instead of ~30–60s full-suite dotnet test

9. (optional) format_check(workspaceId="ws-abc", ...) before committing
```

Two notes on step 5:

- `workspace_reload` is only required between an `Edit` / `Write` and a semantic
  read that must see the new state. Back-to-back `Edit`s don't need a reload
  between them. A `compile_check` after `Edit` without a `workspace_reload` will
  still pick up the change because the file-watcher fires on the write — but the
  defensive call makes the snapshot-freshness explicit and costs ~50ms.
- For worktree sessions running `*_apply` tools (the refactor-first path, e.g.
  `rename_apply`, `extract_type_apply`), the apply pipeline handles the reload
  internally; you only need an explicit `workspace_reload` when mixing `Edit` with
  subsequent MCP calls.

### Main-checkout self-edit variant

If the session is running `dotnet run --project src/RoslynMcp.Host.Stdio` against
the checkout it's editing (not the `.worktrees/<id>/` + installed-tool default),
the only change to the loop above is step 4: replace any `*_apply` with
`Edit` / `Write` (the `*_apply` pipeline would write underneath a stale
`MSBuildWorkspace` snapshot). Every other step — load, read-side navigation,
verify — is identical. `*_preview` remains useful for visualizing the diff
before you hand-edit.

## Known issues (local validation)

- **Parallel test hosts / MSBuild file locks:** If `dotnet test` or `dotnet build` fails with errors copying the test assembly (`RoslynMcp.Tests.dll`) because `testhost.exe` still holds the file, close other test runners or IDE test sessions that loaded that output, then run a full `dotnet test RoslynMcp.slnx --nologo` again from a clean state.
- **Stale `MetadataReference` after a cross-restore reload (rare):** On workspaces where an out-of-process `dotnet restore` bumps a transitive package version between `workspace_load` and a subsequent tool call, the server can — in rare cases — surface spurious `CS1705` "assembly uses higher version" errors from `compile_check`. The Item #7 remediation fires a `WorkspaceReloaded` event that drops the server's per-workspace `Compilation`, diagnostic, DI-scan, and NuGet-vuln caches synchronously with every `workspace_reload`, which eliminates the in-server cache as a suspect. If the symptom still reproduces after a `workspace_reload`, close the workspace and call `workspace_load` again — recreating the underlying `MSBuildWorkspace` forces Roslyn itself to re-resolve every `MetadataReference` from disk.

## Server surface (live counts)

The current stable/experimental tool, resource, and prompt counts are owned by the live `server_info` tool and the `roslyn://server/catalog` resource — query those for an authoritative answer rather than relying on this document. As of catalog `2026.04` (post v1.23.0):

- Stable tools: 104
- Experimental tools: 43
- Stable resources: 9 (3 static + 6 workspace-scoped templates, including the verbose siblings of `roslyn://workspaces` and `roslyn://workspace/{id}/status` added in v1.8 for opt-in full payloads)
- Experimental resources: 4 (includes `roslyn://workspace/{id}/file/{path}/lines/{N-M}` line-range slice added in v1.15)
- Experimental prompts: 20 (includes `refactor_loop` added in v1.18)

Resource discovery for clients that only call `resources/list`: workspace-scoped resources are exposed as URI templates (`roslyn://workspace/{workspaceId}/...`). Read `roslyn://server/resource-templates` for the canonical list of supported URI patterns.

## Workspace session lifetime

- Sessions are kept entirely in-memory by the stdio host. There is **no inactivity TTL** and no idle expiration — `KeyNotFoundException` from a workspace-scoped tool means the host process restarted, `workspace_close` was called, or the concurrent-workspace cap forced an eviction (`ROSLYNMCP_MAX_WORKSPACES`, default 8).
- Recovery is to call `workspace_load` again with the same path; the call is idempotent for repeated loads.
- Some MCP clients (Cursor, Claude Code) may relaunch the stdio server transparently between conversations, which is the most common cause of "lost" sessions.
- **Consumer ordering — call `workspace_load` first.** Transport-reachable does not imply workspace-loaded. Before invoking any workspace-scoped tool (`symbol_search`, `find_references`, `compile_check`, etc.), either (a) call `workspace_load` with the target `.sln` / `.slnx` / `.csproj` path, or (b) poll `server_heartbeat` (cheaper) or the `connection` subfield of `server_info` until `state == "ready"` and `loadedWorkspaceCount >= 1`. The `connection` block also surfaces `stdioPid` and `serverStartedAt` so consumers can correlate readiness with the current host process and detect silent restarts.

## Connection-state signals

Consumers and skills frequently infer "is the server connected / ready?" from the wrong signals. Use the authoritative probes, not filesystem or tool-list side effects.

**Authoritative probes (reliable):**

- `server_info` — full payload including the `connection` subfield (`state`, `stdioPid`, `serverStartedAt`, `loadedWorkspaceCount`). Use when a rich response is needed.
- `server_heartbeat` — lightweight readiness probe added in v1.21.x (`mcp-connection-session-resilience`, PR #218). Use for fast polling / readiness gates.
- Any successful `mcp__roslyn__*` tool call — proves live connectivity by construction.

**NOT reliable connection indicators — do not infer state from these:**

- `mcp-logs-<server>/` cache-directory presence. The directory is created on tool install, not on connect — it persists across disconnects and across host-process restarts.
- **Deferred-tool advertisement** (Claude Code and similar hosts). The host may list `mcp__roslyn__*` tool names in a deferred-tool catalog — visible to `ToolSearch` as names only, with their JSON schemas *not* yet loaded — even when the MCP server is disconnected or has not yet completed its handshake. Seeing `mcp__roslyn__server_info` in the deferred list only proves the host knows the server *exists*, not that it is reachable. Consumers and skills have misread this catalog as "tools ready"; treat it as a registry entry, not a liveness signal. Only a successful `mcp__roslyn__*` call (in particular `server_info` or `server_heartbeat`) proves live connectivity (`dr-9-3-medium-deferred-tool-advertisement-is-a-misleadi`).

## Session And Mutation Safety

- Maintain and pass `workspaceId` for workspace-scoped operations.
- Use preview/apply flows for destructive or broad changes.
- Reject or regenerate previews if workspace version changed.

## Policy Ownership

- Git/worktree/PR behavior: `workflow.md`
- Validation and merge gating: `../CI_POLICY.md`
- Backlog of unfinished work: `backlog.md`
- Human setup, Docker, global tool, CI artifacts: `../docs/setup.md`
