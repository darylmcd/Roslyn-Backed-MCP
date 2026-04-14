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
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `PreviewStoreOptions.MaxEntries` | 20 |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `PreviewStoreOptions.TtlMinutes` | 5 minutes |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `ExecutionGateOptions.RateLimitMaxRequests` | 120 |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `ExecutionGateOptions.RateLimitWindow` | 60 |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `ExecutionGateOptions.RequestTimeout` | 120 |
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

## Roslyn MCP client policy (AI sessions)

Use the **Roslyn MCP server** for C# work in this repository—not only for discovery (navigation, search, diagnostics) but also for **refactoring and other structured edits**.

- **Enable the server:** Repo root `.mcp.json` registers the `roslyn` MCP server (`type: stdio`, `command: roslynmcp`). Cursor may also use a user-level MCP config; keep a `roslyn` / `roslynmcp` entry so agents can reach the same host.
- **Prefer Roslyn tools for C# changes:** When a Roslyn-backed tool exists for the task (for example `rename_*`, `extract_*`, `move_type_*`, `code_fix_*`, `organize_usings_*`, `bulk_replace_type_*`, `split_class_*`), use it instead of hand-editing multiple files or relying on generic text replacement across the solution.
- **Preview before apply:** Use `*_preview` (or equivalent preview flows), review the diff, then call `*_apply` with the returned handles. Align with [Session And Mutation Safety](#session-and-mutation-safety) (workspace id, version checks).
- **Discovery is not a substitute for refactors:** Navigation and read-only tools (`find_references`, `symbol_search`, `go_to_definition`, etc.) inform the plan; they do not replace semantic refactors when a tool implements the change safely.

For tool selection and workflows, see `domains/tool-usage-guide.md`.

## Known issues (local validation)

- **Parallel test hosts / MSBuild file locks:** If `dotnet test` or `dotnet build` fails with errors copying the test assembly (`RoslynMcp.Tests.dll`) because `testhost.exe` still holds the file, close other test runners or IDE test sessions that loaded that output, then run a full `dotnet test RoslynMcp.slnx --nologo` again from a clean state.

## Server surface (live counts)

The current stable/experimental tool, resource, and prompt counts are owned by the live `server_info` tool and the `roslyn://server/catalog` resource — query those for an authoritative answer rather than relying on this document. As of catalog `2026.04`:

- Stable tools: 77
- Experimental tools: 53
- Stable resources: 9 (3 static + 6 workspace-scoped templates, including the verbose siblings of `roslyn://workspaces` and `roslyn://workspace/{id}/status` added in v1.8 for opt-in full payloads)
- Experimental resources: 1 (`roslyn://workspace/{id}/file/{path}/lines/{N-M}` line-range slice added in v1.15)
- Experimental prompts: 19

Resource discovery for clients that only call `resources/list`: workspace-scoped resources are exposed as URI templates (`roslyn://workspace/{workspaceId}/...`). Read `roslyn://server/resource-templates` for the canonical list of supported URI patterns.

## Workspace session lifetime

- Sessions are kept entirely in-memory by the stdio host. There is **no inactivity TTL** and no idle expiration — `KeyNotFoundException` from a workspace-scoped tool means the host process restarted, `workspace_close` was called, or the concurrent-workspace cap forced an eviction (`ROSLYNMCP_MAX_WORKSPACES`, default 8).
- Recovery is to call `workspace_load` again with the same path; the call is idempotent for repeated loads.
- Some MCP clients (Cursor, Claude Code) may relaunch the stdio server transparently between conversations, which is the most common cause of "lost" sessions.

## Session And Mutation Safety

- Maintain and pass `workspaceId` for workspace-scoped operations.
- Use preview/apply flows for destructive or broad changes.
- Reject or regenerate previews if workspace version changed.

## Policy Ownership

- Git/worktree/PR behavior: `workflow.md`
- Validation and merge gating: `../CI_POLICY.md`
- Backlog of unfinished work: `backlog.md`
- Human setup, Docker, global tool, CI artifacts: `../docs/setup.md`
