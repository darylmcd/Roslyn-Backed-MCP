# Runtime

<!-- purpose: Build/test/run commands, execution context, and Roslyn MCP client policy. -->

This document is the canonical runtime and execution-context reference for AI agents and maintainers.

## Execution Context

- Primary runtime target: local stdio host process.
- Workspace model: `MSBuildWorkspace` over on-disk files.
- Unsaved editor buffers are not authoritative for semantic operations.

## Task Runner

Use `just --list` for the full recipe menu. `just ci` is the canonical local CI mirror.

| Recipe | What it does |
|--------|--------------|
| `just build` | Debug build |
| `just test` | Full test suite |
| `just validate` | Fast local check (build + test) |
| `just ci` | Docs check + skills-generic check + release validation + vulnerability audit |
| `just run` | Start the stdio host process |
| `just reinstall` | Reinstall the global tool and Claude Code plugin from this repo |
| `just tool-install-local` | Pack and install the global tool from the local build |
| `just plugin-reload` | Reload the Claude Code plugin from the local checkout |

See `justfile` for the full recipe list, including packaging, Docker, and security audit recipes.

## Platform And Tooling

- .NET SDK: `10.0.100` (`rollForward: latestFeature`) — see `global.json`
- Primary v1 OS target: Windows. macOS and Linux are supported wherever the .NET 10 SDK is available.
- Main local validation entry point: `just ci` (or `./eng/verify-release.ps1` directly)
- Test framework: MSTest (`[TestClass]`, `[TestMethod]`)
- Fast raw commands:
  - `dotnet build RoslynMcp.slnx --nologo`
  - `dotnet test RoslynMcp.slnx --nologo`
  - `dotnet run --project src/RoslynMcp.Host.Stdio`

## Package Identity

- NuGet package ID: `Darylmcd.RoslynMcp`
- CLI command after install: `roslynmcp`
- Install: `dotnet tool install -g Darylmcd.RoslynMcp`
- Update: `dotnet tool update -g Darylmcd.RoslynMcp`

`RoslynMcp.Host.Stdio` is the project/assembly name, not the package ID.

## Environment Variables (stdio host)

Optional overrides are read at startup from `src/RoslynMcp.Host.Stdio/Program.cs`. Numeric values must be positive integers unless noted.

| Variable | Affects | Default |
|----------|---------|---------|
| `ROSLYNMCP_MAX_WORKSPACES` | `WorkspaceManagerOptions.MaxConcurrentWorkspaces` | 8 |
| `ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS` | `WorkspaceManagerOptions.MaxSourceGeneratedDocuments` | 500 |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `ValidationServiceOptions.BuildTimeout` | 5 minutes |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `ValidationServiceOptions.TestTimeout` | 10 minutes |
| `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` | `ValidationServiceOptions.VulnerabilityScanTimeout` | 2 minutes |
| `ROSLYNMCP_MAX_RELATED_FILES` | `ValidationServiceOptions.MaxRelatedFiles` | 25 |
| `ROSLYNMCP_FAST_FAIL_FILE_LOCK` | Early terminate `dotnet test` on MSB3027/MSB3021 file-lock failures | `true` |
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `PreviewStoreOptions.MaxEntries` | 20 |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `PreviewStoreOptions.TtlMinutes` | 5 minutes |
| `ROSLYNMCP_PREVIEW_PERSIST_DIR` | Persist composite preview tokens for cross-process apply flows | unset |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `ExecutionGateOptions.RateLimitMaxRequests` | 120 |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `ExecutionGateOptions.RateLimitWindow` | 60 |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `ExecutionGateOptions.RequestTimeout` | 120 |
| `ROSLYNMCP_ON_STALE` | `ExecutionGateOptions.OnStale` (`auto-reload`, `warn`, `off`) | `auto-reload` |
| `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` | `SecurityOptions.PathValidationFailOpen` | `false` |
| `ROSLYNMCP_SCRIPT_MAX_CONCURRENT` | `ScriptingServiceOptions.MaxConcurrentEvaluations` | 4 |
| `ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS` | `ScriptingServiceOptions.ConcurrencySlotAcquireTimeoutSeconds` | 5 seconds |
| `ROSLYNMCP_SCRIPT_MAX_ABANDONED` | `ScriptingServiceOptions.MaxAbandonedEvaluations` | 8 |
| `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` | `ScriptingServiceOptions.TimeoutSeconds` | 10 seconds |
| `ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS` | `ScriptingServiceOptions.WatchdogGraceSeconds` | 10 seconds |

## Claude Code Plugin

Plugin-relevant files in this repo:

- `.claude-plugin/` — plugin manifest and marketplace descriptor
- `skills/` — bundled skill prompts
- `hooks/` — PreToolUse and PostToolUse safety hooks
- `.mcp.json` — repo-local MCP declaration; may include literal `env` overrides for project-specific tuning

Install via:

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

Copy-ready `.mcp.json` examples live under `docs/mcp-json-examples/`.

## MCP Runtime Notes

- `stdout` is reserved for MCP protocol traffic.
- Operational logging goes to `stderr`.
- For custom stdio clients, see `../docs/stdio-client-integration.md`.

## Roslyn MCP Client Policy (AI sessions)

The Roslyn MCP server is the default tool surface for C# work in this repository: navigation, search, diagnostics, verification, and covered refactoring flows.

### Read-side default

Prefer Roslyn MCP read tools over shell/Grep in normal sessions, including bootstrap self-edit:

- `compile_check` over `dotnet build`
- `test_related_files` + `test_run --filter` over broad `dotnet test`
- `find_references`, `find_consumers`, `find_implementations` over text search
- `symbol_search` and `document_symbols` over manual file scanning

Canonical pattern-to-tool mappings live in `bootstrap-read-tool-primer.md`.

### Parallel read-only calls

When the client or host can issue multiple MCP requests concurrently, read-only
workspace tools may fan out in parallel against the same loaded workspace. The
server's `WorkspaceExecutionGate` allows overlapping reads on one workspace and
serializes writers/lifecycle calls around them.

- Safe parallel fan-out candidates: `symbol_search`, `find_references`,
  `find_consumers`, `document_symbols`, `compile_check`, and other read-only
  navigation/analysis calls after `workspace_load`.
- Do not overlap reads with write/lifecycle operations on the same workspace:
  `*_apply`, `apply_*`, `workspace_load`, `workspace_reload`, and
  `workspace_close` stay serialized by design.
- If the host serializes MCP tool calls, expect no speedup. That is a client
  limitation, not a server-side correctness bug.

### Write-side by session shape

| Session shape | Preferred write path |
|---------------|----------------------|
| Peer repo or worktree self-edit against the installed global tool | Use Roslyn MCP preview → apply when the refactor tool covers the operation |
| Main-checkout self-edit against `dotnet run --project src/RoslynMcp.Host.Stdio` | Use `*_preview` for diff visualization, then `Edit` / `Write`; do not run `*_apply` against the checkout-under-build |
| Server disconnected | State that the declared server is unavailable and use the documented fallback workflow |

### Verification loop

Default post-edit verify:

1. `compile_check`
2. `test_related_files` -> `test_run --filter`
3. `format_check`, or `validate_recent_git_changes` / `validate_workspace` when the bundled verify shape fits better

For the long-form decision tree, use `domains/tool-usage-guide.md`.

## Workspace Session Lifetime

- Sessions are kept in memory by the stdio host. There is no inactivity TTL.
- If a workspace-scoped tool reports that the workspace is missing, the usual causes are host restart, `workspace_close`, or eviction at the concurrent-workspace cap.
- Recovery is `workspace_load` on the same path; repeated loads are idempotent.
- Call `workspace_load` before workspace-scoped tools, or poll `server_heartbeat` / `server_info.connection` until the server reports a loaded workspace.

## Connection-State Signals

Authoritative probes:

- `server_info`
- `server_heartbeat`
- Any successful `mcp__roslyn__*` call

Do not infer liveness from deferred-tool catalogs or cache-directory presence.

## Session And Mutation Safety

- Maintain and pass `workspaceId` for workspace-scoped operations.
- Use preview/apply flows for destructive or broad changes.
- Reject or regenerate previews if the workspace version changed.

## Policy Ownership

- Git/worktree/PR behavior: `workflow.md`
- Validation and merge gating: `../CI_POLICY.md`
- Backlog of unfinished work: `backlog.md`
- Human setup, Docker, global tool, and CI artifacts: `../docs/setup.md`
