# Runtime

<!-- purpose: Build/test/run commands, execution context, and Roslyn MCP client policy. -->

This document is the canonical runtime and execution-context reference for AI agents and maintainers.

## Execution Context

- Primary runtime target: local stdio host process.
- Workspace model: `MSBuildWorkspace` over on-disk files.
- Unsaved editor buffers are not authoritative for semantic operations.

## Task Runner

Use `just --list` for the full recipe menu. `just ci` is the canonical local CI mirror; its measured cost, timeout/background mode, hook runtime, exact test filter, regeneration companions, flake registry, and concurrency safety live in [AGENTS.md § Validation runtime](../AGENTS.md#validation-runtime).

| Recipe | What it does |
|--------|--------------|
| `just build` | Debug build |
| `just test` | Full test suite |
| `just validate` | Fast local check (build + test) |
| `just ci` | Required PR-equivalent gate: docs + shipped skills + release validation without coverage/network tests + vulnerability audit |
| `just full` | Informational full lane: coverage + live network tests + publish/hash checks + docs/skills + vulnerability audit |
| `just run` | Start the stdio host process |
| `just reinstall` | Reinstall the global tool and Claude Code plugin from this repo |
| `just tool-install-local` | Pack and install the global tool from the local build |
| `just plugin-reload` | Reload the Claude Code plugin from the local checkout |
| `pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1` | Validate fragment grammar and require a changed fragment for change-bearing work |

See `justfile` for the full recipe list, including packaging, Docker, and security audit recipes.

## Platform And Tooling

- .NET SDK: `10.0.400` (`rollForward: latestFeature`) — see `global.json`; CI also runs an exact-floor build/workspace probe
- Primary v1 OS target: Windows. macOS and Linux are supported wherever the .NET 10 SDK is available.
- Main local validation entry point: `just ci`; use the canonical runtime row in [AGENTS.md § Validation runtime](../AGENTS.md#validation-runtime) before invoking it. Use `just full` only when the informational coverage/live-network lane is required.
- Test framework: MSTest (`[TestClass]`, `[TestMethod]`)
- Fast raw commands:
  - `dotnet build RoslynMcp.slnx --nologo`
  - `dotnet test RoslynMcp.slnx --nologo`
  - `dotnet run --project src/RoslynMcp.Host.Stdio`
  - `pwsh ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests -TestShardOnly -TestShardIndex 0 -TestShardCount 2` — reproduce one CI non-owner class shard without duplicate policy/publish work; use the complementary zero-based index for the other half. Omit `-TestShardOnly` for a standalone full release gate.
  - `pwsh ./eng/get-test-shard-plan.ps1 -TestAssemblyPath <RoslynMcp.Tests.dll> -TestShardCount 2 -TestShardIndex 0` — inspect the deterministic complete/disjoint class manifest as JSON. Discovery and integrity failures exit nonzero.

## Package Identity

- NuGet package ID: `Darylmcd.RoslynMcp`
- CLI command after install: `roslynmcp`
- Install: `dotnet tool install -g Darylmcd.RoslynMcp`
- Update: `dotnet tool update -g Darylmcd.RoslynMcp`

`RoslynMcp.Host.Stdio` is the project/assembly name, not the package ID.

## Environment Variables

All `ROSLYNMCP_*` overrides (defaults, affected options): [references/environment-variables.md](references/environment-variables.md).

## Claude Code Plugin

Plugin-relevant files in this repo:

- `.claude-plugin/` — plugin manifest, marketplace descriptor, and `mcp.json` (launches the release-matched `Darylmcd.RoslynMcp` package through `dnx` with `ROSLYNMCP_SANCTIONED_ROOTS`)
- `skills/` — bundled skill prompts
- `hooks/` — PreToolUse and PostToolUse safety hooks
- MCP client registration: the plugin's `.claude-plugin/mcp.json` or user/session-scoped client config. Root `.mcp.json` is gitignored and not shipped; tracked `.cursor/mcp.json` and `.vscode/mcp.json` register a bare `roslynmcp` command. A config file's presence is not liveness evidence.

Install via:

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

Copy-ready `.mcp.json` examples live under `docs/mcp-json-examples/`.

## MCP Runtime Notes

- `stdout` is reserved for MCP protocol traffic.
- Operational logging goes to `stderr`; the opt-in file sink is additive.
- For custom stdio clients and the consumer-facing observability contract, see `../docs/stdio-client-integration.md#operator-observability`.

## Roslyn MCP Client Policy (AI sessions)

The Roslyn MCP server is the expected tool surface for C# work here: navigation, search, diagnostics, verification, and covered refactoring flows.

Client configuration declares intent; only a successful `server_heartbeat`, `server_info`, or other `mcp__roslyn__*` call verifies liveness.

### Read-side default

Prefer Roslyn MCP read tools over shell/Grep in normal sessions, including bootstrap self-edit:

- `compile_check` over `dotnet build`
- `test_related_files` + `test_run --filter` over broad `dotnet test`
- `find_references`, `find_consumers`, `find_implementations` over text search
- `symbol_search` and `document_symbols` over manual file scanning

Canonical pattern-to-tool mappings live in `bootstrap-read-tool-primer.md`.

### Parallel read-only calls

`WorkspaceExecutionGate` allows overlapping reads on one loaded workspace and serializes writers/lifecycle calls around them.

- Safe to fan out: read-only navigation/analysis/verification calls (`symbol_search`, `find_references`, `document_symbols`, `compile_check`, ...) after `workspace_load`.
- Never overlap with reads: `*_apply`, `apply_*`, `workspace_load`, `workspace_reload`, `workspace_close`.
- A host that serializes MCP calls yields no speedup; that is a client limitation.
- Usage detail: [domains/tool-usage-guide.md](domains/tool-usage-guide.md#parallel-read-fan-out).

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
- Call `workspace_load` before workspace-scoped tools. An idle server does not load a workspace by itself; poll `server_heartbeat` / `server_info.connection` only to observe a load that another caller has already initiated.
- The required load argument is `path`: `{"path":"C:/Code-Repo/Roslyn-Backed-MCP/RoslynMcp.slnx"}` for this checkout. Resolve the current worktree path when working elsewhere; do not substitute `solutionPath`. See `bootstrap-read-tool-primer.md` for argument-error and analyzer-readiness recovery.

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
