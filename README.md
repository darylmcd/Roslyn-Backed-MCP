# Roslyn-Backed MCP Server

[![CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-submission%20pending-lightgrey)](https://registry.modelcontextprotocol.io/)

Local-first MCP (Model Context Protocol) server for semantic C# analysis, navigation, validation, and refactoring on real `.sln` / `.slnx` / `.csproj` workspaces. It uses Roslyn and `MSBuildWorkspace`, runs over stdio, and does not require Visual Studio.

> **Correct package ID:** `Darylmcd.RoslynMcp` &nbsp;·&nbsp; **CLI:** `roslynmcp` &nbsp;·&nbsp; **Plugin:** `roslyn-mcp@roslyn-mcp-marketplace`

## What It Does

- Loads real C# solutions and projects with session-scoped `workspaceId`s.
- Exposes semantic navigation, diagnostics, build/test helpers, and preview/apply refactoring workflows over MCP.
- Ships as a .NET global tool, a Claude Code plugin, and a source-buildable stdio host.
- Publishes the authoritative live surface through `server_info` and `roslyn://server/catalog`.

## Why Roslyn-Backed MCP

- **No Visual Studio dependency** — runs anywhere the .NET SDK runs (Windows, macOS, Linux, containers, CI).
- **Production ops discipline** — repeatable CI mirror (`just ci`), release verification scripts, and a documented two-layer update story for the global tool *and* the Claude Code plugin.
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

### Option B — Zero-Install Via `dnx` (.NET 10)

`dnx` is the .NET SDK's `npx`-equivalent: it resolves a tool package from NuGet on demand, without installing a global shim. Requires **.NET 10 SDK Preview 6 or later** (`dnx` ships with the SDK).

One-shot smoke test:

```bash
dnx Darylmcd.RoslynMcp --yes
```

The process should start and then appear to hang — that's expected; it's an MCP server waiting for protocol messages on stdin. The `--yes` flag is **mandatory** under MCP hosts because there is no TTY for the interactive install-consent prompt.

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
        "https://api.nuget.org/v3/index.json",
        "--yes"
      ]
    }
  }
}
```

Trade-offs vs. the global tool:

- ✅ No PATH pollution; no manual install step.
- ✅ Each cold start resolves to the latest version unless pinned (no `dotnet tool update` step).
- ⚠️ Cold-start cost on first invocation while the package downloads.
- ⚠️ For reproducible setups, pin the version: add `"--version", "1.35.0"` to `args`.

A copy-paste config also lives at [`docs/mcp-json-examples/dnx.mcp.json`](docs/mcp-json-examples/dnx.mcp.json).

### Option C — Claude Code Plugin

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

The plugin bundles the MCP server, 32 skills, and safety hooks. For packaging, reinstall, and local plugin-dev details, see [docs/setup.md](docs/setup.md) and [docs/reinstall.md](docs/reinstall.md).

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

### MCP Registry (submission pending)

The server is also being published to the public [MCP Registry](https://registry.modelcontextprotocol.io/). Once the submission is approved, MCP-Registry-aware clients will be able to discover and install the server by name.

Draft manifest: [`.claude-plugin/server.json`](.claude-plugin/server.json) — published name `io.github.darylmcd/roslyn-mcp`, NuGet package `Darylmcd.RoslynMcp`, runtime `dnx`.

Until the submission is approved, prefer the [Global Tool](#install-as-a-global-tool) or [Claude Code Plugin](#claude-code-plugin) install paths above. After approval, this section will be updated with the registry URL and the registry-aware client install snippet.

## Configuration

The server starts with built-in defaults. To override `ROSLYNMCP_*` values for a repo, add a project-scope `.mcp.json` with literal `env` values.

| Variable | Default | Purpose |
|----------|---------|---------|
| `ROSLYNMCP_MAX_WORKSPACES` | `8` | Concurrent workspace cap |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `300` | Build timeout |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `600` | Test timeout |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `5` | Preview-token TTL |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `120` | Per-request timeout |

Copy-ready examples live in [docs/mcp-json-examples/README.md](docs/mcp-json-examples/README.md). The full runtime/config surface is documented in [ai_docs/runtime.md](ai_docs/runtime.md).

## Security

Loading a solution or project executes MSBuild evaluation. Treat workspaces as trusted code unless you run the server inside a sandbox, container, or VM.

- Only load repos you trust.
- Use isolation for untrusted workspaces.
- Path validation is defense in depth, not a substitute for trusting the loaded project graph.

See [SECURITY.md](SECURITY.md) for disclosure policy.

## Live Surface

The current release exposes **171 tools** (111 stable / 60 experimental), **13 resources** (9 stable / 4 experimental), and **20 prompts** (all experimental).

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
