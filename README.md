# Roslyn-Backed MCP Server

[![CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-listed-blue)](https://registry.modelcontextprotocol.io/)

Local-first MCP (Model Context Protocol) server for semantic C# analysis, navigation, validation, and refactoring on real `.sln` / `.slnx` / `.csproj` workspaces. It uses Roslyn and `MSBuildWorkspace`, runs over stdio, and does not require Visual Studio. Refactoring tools follow a preview → apply discipline: previews return short-lived tokens, so agents can dry-run multi-file edits before mutating the workspace. It ships as a .NET global tool, a Claude Code plugin, and a source-buildable stdio host.

> **Correct package ID:** `Darylmcd.RoslynMcp` &nbsp;·&nbsp; **CLI:** `roslynmcp` &nbsp;·&nbsp; **Plugin:** `roslyn-mcp@roslyn-mcp-marketplace`

## Quick Start

Prerequisite: the [.NET 10 SDK](https://dotnet.microsoft.com/download).

### Option A — Install As A Global Tool

```bash
dotnet tool install -g Darylmcd.RoslynMcp
```

Update later with `dotnet tool update -g Darylmcd.RoslynMcp`.

> **Configure a filesystem boundary before first use.** The global tool ships a binary, not a
> config, and an unset boundary is **fail-closed**: every path-taking tool rejects its input.
> Set `ROSLYNMCP_SANCTIONED_ROOTS` in your MCP client config (for example a project-scope `.mcp.json`):
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
> `.` resolves against the server process's working directory, which your MCP client chooses; use an
> absolute path to pin the boundary. See [Setup](docs/setup.md#configure-the-filesystem-boundary).

### Option B — Zero-Install Via `dnx` (.NET 10)

`dnx` resolves the package from NuGet on demand, without installing a global shim: run `dnx Darylmcd.RoslynMcp`. Pin a version for reproducible setups with `Darylmcd.RoslynMcp@<version>`. Configure the same `ROSLYNMCP_SANCTIONED_ROOTS` boundary as above; copy-paste configs live in [`docs/mcp-json-examples/`](docs/mcp-json-examples/README.md), and details are in [Setup](docs/setup.md#zero-install-via-dnx).

### Option C — Claude Code Plugin

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

The plugin bundles 32 skills and safety hooks, then launches the exact release-matched `Darylmcd.RoslynMcp` package through `dnx`; it does not require a global `roslynmcp` shim. The first launch requires NuGet access unless the package is already cached. The plugin's bundled `"."` boundary resolves to the session's working directory, so start Claude Code inside the repo you want to analyze; see [Setup](docs/setup.md#claude-code-plugin) for the other supported overrides.

### Per-Client Config

The JSON shape is the same across MCP clients (VS Code uses a top-level `"servers"` key); only the config file path differs. See the [compatibility matrix](docs/compatibility.md) for per-client paths and a health-check prompt, and [`docs/mcp-json-examples/`](docs/mcp-json-examples/README.md) for copy-ready snippets.

## Live Surface

The current release exposes **175 tools** (113 stable / 62 experimental), **14 resources** (9 stable / 5 experimental), and **20 prompts** (all experimental).

Use the running server for the authoritative live catalog and support tiers:

- `server_info` for a human-readable summary
- `roslyn://server/catalog` for the machine-readable contract
- `roslyn://server/resource-templates` for resource URI templates

Stable families include workspace/session management, semantic navigation, diagnostics, build/test helpers, and preview/apply refactoring flows. Experimental families include broader project mutation, scaffolding, orchestration, direct text-edit helpers, and prompts. Set `ROSLYNMCP_TOOL_TIERS=stable` to register only the closed stable workflow (currently 94 callable tools) for clients that eagerly load tool definitions.

## Configuration And Security

All operational settings are optional `ROSLYNMCP_*` environment variables with compiled-in defaults; the full reference is in [docs/setup.md](docs/setup.md#environment-variables). The exception is `ROSLYNMCP_SANCTIONED_ROOTS`, which you must set (see above).

Loading a solution or project executes MSBuild evaluation: only load repositories you trust, or run the server in a sandbox. The filesystem boundary is server-owned; a client's MCP Roots can only narrow it. See [docs/setup.md](docs/setup.md#security-model) and [SECURITY.md](SECURITY.md) for the disclosure policy. Upgrading from 2.x? Read the [migration notes](docs/setup.md#upgrading-from-2x) first.

## Docs

- [docs/README.md](docs/README.md) — index of everything under `docs/`
- [docs/setup.md](docs/setup.md) — packaging, install options, configuration, security model, plugin install, CI artifacts
- [docs/compatibility.md](docs/compatibility.md) — MCP client compatibility matrix and health check
- [docs/stdio-client-integration.md](docs/stdio-client-integration.md) — custom MCP client integration
- [docs/product-contract.md](docs/product-contract.md) — stable vs experimental surface contract
- [docs/release-policy.md](docs/release-policy.md) — release gates and compatibility rules
- [CONTRIBUTING.md](CONTRIBUTING.md) — build from source, tests, and filing surface-test findings
- [AGENTS.md](AGENTS.md) — bootstrap entry point for AI agents working in this repo
- [ai_docs/README.md](ai_docs/README.md) — canonical AI-doc routing index

## Support

- Bugs and feature requests: [GitHub Issues](https://github.com/darylmcd/Roslyn-Backed-MCP/issues)
- Contribution guidelines: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security disclosures: [SECURITY.md](SECURITY.md)
