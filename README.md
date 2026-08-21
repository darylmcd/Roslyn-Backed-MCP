# Roslyn-Backed MCP Server

[![CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-listed-blue)](https://registry.modelcontextprotocol.io/)

Local-first MCP (Model Context Protocol) server for semantic C# analysis, navigation, validation, and refactoring on real `.sln` / `.slnx` / `.csproj` workspaces. It uses Roslyn and `MSBuildWorkspace`, runs over stdio, and does not require Visual Studio.

> **Correct package ID:** `Darylmcd.RoslynMcp` &nbsp;·&nbsp; **CLI:** `roslynmcp` &nbsp;·&nbsp; **Plugin:** `roslyn-mcp@roslyn-mcp-marketplace`

## What It Does

- Loads real C# solutions and projects with session-scoped `workspaceId`s.
- Exposes semantic navigation, diagnostics, build/test helpers, and preview/apply refactoring workflows over MCP.
- Ships as a .NET global tool, a Claude Code plugin, and a source-buildable stdio host.
- Publishes the authoritative live surface through `server_info` and `roslyn://server/catalog`.

## Why Roslyn-Backed MCP

- **No Visual Studio dependency** — runs anywhere the .NET SDK runs (Windows, macOS, Linux, containers, CI).
- **Production ops discipline** — repeatable CI mirror (`just ci`), release verification scripts, and documented update paths for the global tool and release-pinned Claude Code plugin.
- **Safe install defaults** — no `${user_config.*}` placeholder substitution that breaks prompt-skipping install flows; the server starts with compiled-in defaults and accepts literal overrides via project-scope `.mcp.json`.
- **Authoritative live surface** — every release publishes `server_info` + `roslyn://server/catalog` so clients can discover the exact tool/resource/prompt set and support tier (stable vs experimental) without guessing.
- **Preview → apply discipline** — refactoring tools issue preview tokens with TTLs and a verify step before mutating the workspace, so agents can dry-run multi-file edits.
- **Three install paths** — pick one: global tool (`dotnet tool install`), zero-install via `dnx` (.NET 10), or the Claude Code plugin.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — pinned to `10.0.100` in [`global.json`](global.json) (`rollForward: latestFeature`)

### Option A — Install As A Global Tool

```bash
dotnet tool install -g Darylmcd.RoslynMcp
```

- Package ID: `Darylmcd.RoslynMcp`
- CLI command: `roslynmcp`
- Updates: `dotnet tool update -g Darylmcd.RoslynMcp`

> **Configure a filesystem boundary before first use.** The global tool ships a binary, not a
> config — you write your own `.mcp.json`, and an unset boundary is **fail-closed**: every
> path-taking tool (`workspace_load`, edits, symbol lookups) rejects its input, and solution
> discovery returns nothing. Set `ROSLYNMCP_SANCTIONED_ROOTS` in your client config:
>
> ```json
> {
>   "mcpServers": {
>     "roslyn": {
>       "type": "stdio",
>       "command": "roslynmcp",
>       "env": { "ROSLYNMCP_SANCTIONED_ROOTS": "." }
>     }
>   }
> }
> ```
>
> `.` resolves against the server process's working directory, which your MCP client chooses —
> use an absolute path if you want the boundary pinned regardless of how the server is launched.
> See [Configuration](#configuration). The Claude Code plugin and Desktop extension ship this
> default already; only hand-written configs need it.

### Option B — Zero-Install Via `dnx` (.NET 10)

`dnx` is the .NET SDK's `npx`-equivalent: it resolves a tool package from NuGet on demand, without installing a global shim. Requires **.NET 10 SDK 10.0.100 or later** (`dnx` ships with the SDK).

One-shot smoke test:

```bash
dnx Darylmcd.RoslynMcp
```

The process should start and then appear to hang — that's expected; it's an MCP server waiting for protocol messages on stdin. `dnx` is noninteractive unless `--interactive` is explicitly requested, so MCP hosts need no consent flag.

`.mcp.json` snippet:

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "dnx",
      "args": [
        "Darylmcd.RoslynMcp",
        "--source",
        "https://api.nuget.org/v3/index.json"
      ]
    }
  }
}
```

Trade-offs vs. the global tool:

- ✅ No PATH pollution; no manual install step.
- ✅ Each cold start resolves to the latest version unless pinned (no `dotnet tool update` step).
- ⚠️ Cold-start cost on first invocation while the package downloads.
- ⚠️ For reproducible setups, pin the version in the package token: `Darylmcd.RoslynMcp@<version>`.

A copy-paste config also lives at [`docs/mcp-json-examples/dnx.mcp.json`](docs/mcp-json-examples/dnx.mcp.json).

### Option C — Claude Code Plugin

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

The plugin bundles 32 skills and safety hooks, then launches the exact release-matched `Darylmcd.RoslynMcp` package through `dnx`; it does not require a global `roslynmcp` shim. The first launch requires NuGet access unless the package is already cached. For packaging, reinstall, and local plugin-dev details, see [docs/setup.md](docs/setup.md) and [docs/reinstall.md](docs/reinstall.md).

### Build And Run From Source

```bash
dotnet build RoslynMcp.slnx --nologo
dotnet test RoslynMcp.slnx --nologo
dotnet run --project src/RoslynMcp.Host.Stdio
```

### Per-Client Config

The JSON shape is the same across MCP clients — only the file path differs. Drop one of the [`docs/mcp-json-examples/`](docs/mcp-json-examples/) snippets into the right location for your client:

| Client | Config file | Notes |
|--------|-------------|-------|
| Claude Code | `.mcp.json` (repo root) | Project-scope; pairs naturally with the Claude Code Plugin path above. |
| Cursor | `.cursor/mcp.json` (repo root) or `~/.cursor/mcp.json` (global) | Project-scope wins over global. |
| VS Code (MCP-aware) | `.vscode/mcp.json` (repo root) | Workspace-scope; restart the MCP host after editing. |
| Claude Desktop | `claude_desktop_config.json` (per-OS app-data dir) | Global only; no project-scope config. |

Minimal config (works with **Option A** — global tool):

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "roslynmcp"
    }
  }
}
```

For the **Option B** (`dnx`) form, use the snippet from the previous section or copy [`docs/mcp-json-examples/dnx.mcp.json`](docs/mcp-json-examples/dnx.mcp.json).

For NDJSON framing, handshake order, and minimal Python/C# client examples, see [docs/stdio-client-integration.md](docs/stdio-client-integration.md).

### Health Check

After installing and wiring up `.mcp.json`, paste this single prompt into your MCP client to verify the server is reachable and report its surface:

> Call `server_info` and read the `roslyn://server/catalog` resource. Report back:
> 1. The server name and version.
> 2. The total tool / resource / prompt counts and their stable-vs-experimental split.
> 3. Whether any workspaces are currently loaded (and their IDs if so).
> 4. Any warnings or degraded-state flags.

If both calls succeed and the version matches what you installed, the install is healthy.

### MCP Registry

The server is published to the official [MCP Registry](https://registry.modelcontextprotocol.io/) under the name **`io.github.darylmcd/roslyn-mcp`**. MCP-Registry-aware clients — and the downstream catalogs that mirror the registry (the GitHub MCP Registry, the VS Code and Visual Studio MCP galleries, and aggregators) — can discover and install the server by name.

Manifest: [`.claude-plugin/server.json`](.claude-plugin/server.json) — name `io.github.darylmcd/roslyn-mcp`, NuGet package `Darylmcd.RoslynMcp`, runtime `dnx`. Every release tag republishes it automatically via the `publish-nuget` workflow using GitHub OIDC.

You can also install directly without a registry-aware client via the [Global Tool](#option-a--install-as-a-global-tool) or [Claude Code Plugin](#option-c--claude-code-plugin) paths above.

## Configuration

The server starts with built-in operational defaults. File-path access is the exception: configure
`ROSLYNMCP_SANCTIONED_ROOTS` explicitly (usually `.` in a project-scope `.mcp.json`). Multiple roots
use the platform path separator (`;` on Windows, `:` on macOS/Linux). An empty root list fails
closed; see [Setup](docs/setup.md#configure-the-filesystem-boundary). Other `ROSLYNMCP_*` values
remain optional literal `env` overrides.

| Variable | Default | Purpose |
|----------|---------|---------|
| `ROSLYNMCP_SANCTIONED_ROOTS` | empty (deny path access) | Server-owned path-validation and solution-discovery boundary |
| `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` | `false` | Temporary compatibility escape hatch for the **empty**-boundary case only: allows path access when no roots are configured. It never bypasses a non-empty boundary. Prefer configuring roots |
| `ROSLYNMCP_ALLOW_ROOT_EXPANSION` | `false` | Allows a request with `expandSanctionedRoots=true` to reach sibling worktrees under each sanctioned root's immediate parent; both opt-ins are required |
| `ROSLYNMCP_MAX_WORKSPACES` | `8` | Concurrent workspace cap |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `300` | Build timeout |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `600` | Test timeout |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `5` | Preview-token TTL |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `120` | Per-request timeout |
| `ROSLYNMCP_TOOL_TIERS` | `stable,experimental` | Registered MCP surface tiers; set `stable` to expose only stable tools, prompts, and resources (currently 113 tools) to clients that eagerly load discovery definitions; experimental requires the stable baseline |

Copy-ready examples live in [docs/mcp-json-examples/README.md](docs/mcp-json-examples/README.md). The full runtime/config surface is documented in [ai_docs/runtime.md](ai_docs/runtime.md).

## Security

Loading a solution or project executes MSBuild evaluation. Treat workspaces as trusted code unless you run the server inside a sandbox, container, or VM.

- Only load repos you trust.
- Use isolation for untrusted workspaces.
- Path validation is defense in depth, not a substitute for trusting the loaded project graph.

**The filesystem boundary is server-owned.** `ROSLYNMCP_SANCTIONED_ROOTS` is configured by you, the
operator — not by the connecting client. A client's MCP Roots can only *narrow* that boundary; they
can never widen it or act as the sole authority. This is deliberate: the control exists to constrain
the **agent**, so a model that is confused or prompt-injected into reading outside your project
cannot do so, even if it asks. Sibling-worktree widening needs two independent opt-ins — the server
operator setting `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` *and* the request setting
`expandSanctionedRoots=true` — so request input alone never widens access.

Paths are canonicalized component-by-component, resolving every symlink and junction in the ancestor
chain before comparison, so a file under a linked ancestor cannot present an in-boundary logical path
while pointing outside it.

See [SECURITY.md](SECURITY.md) for disclosure policy.

## Upgrading From 2.x

Path validation is now bounded by a server-owned root list instead of the client's (deprecated)
`roots/list` capability. Two things to do before upgrading:

1. **Set `ROSLYNMCP_SANCTIONED_ROOTS`.** An unset boundary is fail-closed — every path-taking tool
   rejects its input. `.` is the normal project-scoped value; see [Configuration](#configuration) for
   the delimiter and [Option A](#option-a--install-as-a-global-tool) for a copy-ready snippet. If you
   need to defer, `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` restores the old unbounded behavior as a
   temporary measure. From this release the server warns at startup and reports
   `server_info.pathBoundary` when the boundary is missing, so the state is visible before your first
   call rather than after it.
2. **Stop relying on `roots/list` for discovery.** Query-anchored solution discovery no longer calls
   it and scans only configured roots. Pass a file-path argument, configure a root containing exactly
   one solution, call `workspace_load` explicitly, or pass a `workspaceId`.

If you install via the Claude Code plugin or the Desktop extension, both ship the default — but
update **both** layers together. A binary-only update leaves a stale config with no boundary set,
which is the fail-closed case above. Rationale and full detail:
[ADR 0002](docs/decisions/0002-configured-sanctioned-root-boundary.md).

## Live Surface

The current release exposes **173 tools** (113 stable / 60 experimental), **14 resources** (9 stable / 5 experimental), and **20 prompts** (all experimental).

Use the running server for the authoritative live catalog and support tiers:

- `server_info` for a human-readable summary
- `roslyn://server/catalog` for the machine-readable contract
- `roslyn://server/resource-templates` for resource URI templates

Stable families include workspace/session management, semantic navigation, diagnostics, build/test helpers, and preview/apply refactoring flows. Experimental families include broader project mutation, scaffolding, orchestration, direct text-edit helpers, and prompts.

## Repository Layout

- `src/RoslynMcp.Host.Stdio/` — stdio host, tool/resource/prompt wiring, logging
- `src/RoslynMcp.Core/` — DTOs, contracts, abstractions, preview-store types
- `src/RoslynMcp.Roslyn/` — Roslyn workspace, analysis, diagnostics, refactoring, execution services
- `tests/RoslynMcp.Tests/` — integration and regression coverage
- `skills/` — bundled Claude Code skill definitions
- `hooks/` — Claude Code safety hooks

## Docs

- [docs/setup.md](docs/setup.md) — packaging, Docker, tool install, plugin install, CI artifacts
- [docs/compatibility.md](docs/compatibility.md) — MCP client compatibility matrix (Claude Code, Cursor, VS Code, Claude Desktop)
- [docs/stdio-client-integration.md](docs/stdio-client-integration.md) — custom MCP client integration
- [docs/product-contract.md](docs/product-contract.md) — stable vs experimental surface contract
- [docs/release-policy.md](docs/release-policy.md) — release gates and compatibility rules
- [AGENTS.md](AGENTS.md) — bootstrap entry point for AI agents working in this repo
- [ai_docs/README.md](ai_docs/README.md) — canonical AI-doc routing index

## Filing Surface-Test Findings

If you find a bug or behaviour gap while running [`/mcp-server-surface-test`](skills/mcp-server-surface-test/README.md) against your own C# repo, share it back via the [Surface-test finding](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/new?template=mcp-server-surface-test-finding.yml) issue template. The shipped skill renders findings into a copy-paste body block by default; pass `--auto-file` and the skill calls `gh issue create` for you.

P0 / `area: security` findings are refused for public filing — see [SECURITY.md](SECURITY.md) for the private-disclosure path.

## Support

- Bugs and feature requests: [GitHub Issues](https://github.com/darylmcd/Roslyn-Backed-MCP/issues)
- Contribution guidelines: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security disclosures: [SECURITY.md](SECURITY.md)
