# Roslyn-Backed MCP Server

[![CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml/badge.svg)](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/workflows/ci.yml)

Local-first MCP (Model Context Protocol) server for semantic C# analysis, navigation, validation, and refactoring on real `.sln` / `.slnx` / `.csproj` workspaces. It uses Roslyn and `MSBuildWorkspace`, runs over stdio, and does not require Visual Studio.

## What It Does

- Loads real C# solutions and projects with session-scoped `workspaceId`s.
- Exposes semantic navigation, diagnostics, build/test helpers, and preview/apply refactoring workflows over MCP.
- Ships as a .NET global tool, a Claude Code plugin, and a source-buildable stdio host.
- Publishes the authoritative live surface through `server_info` and `roslyn://server/catalog`.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — pinned to `10.0.100` in [`global.json`](global.json) (`rollForward: latestFeature`)

### Install As A Global Tool

```bash
dotnet tool install -g Darylmcd.RoslynMcp
```

- Package ID: `Darylmcd.RoslynMcp`
- CLI command: `roslynmcp`

### Build And Run From Source

```bash
dotnet build RoslynMcp.slnx --nologo
dotnet test RoslynMcp.slnx --nologo
dotnet run --project src/RoslynMcp.Host.Stdio
```

### Claude Code Plugin

```text
/plugin marketplace add darylmcd/Roslyn-Backed-MCP
/plugin install roslyn-mcp@roslyn-mcp-marketplace
```

The plugin bundles the MCP server, 31 skills, and safety hooks. For packaging, reinstall, and local plugin-dev details, see [docs/setup.md](docs/setup.md) and [docs/reinstall.md](docs/reinstall.md).

### Any Stdio MCP Client

Point the client at the installed tool:

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

For NDJSON framing, handshake order, and minimal Python/C# client examples, see [docs/stdio-client-integration.md](docs/stdio-client-integration.md).

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

The current release exposes **167 tools** (111 stable / 56 experimental), **13 resources** (9 stable / 4 experimental), and **20 prompts** (all experimental).

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
- [docs/stdio-client-integration.md](docs/stdio-client-integration.md) — custom MCP client integration
- [docs/product-contract.md](docs/product-contract.md) — stable vs experimental surface contract
- [docs/release-policy.md](docs/release-policy.md) — release gates and compatibility rules
- [AGENTS.md](AGENTS.md) — bootstrap entry point for AI agents working in this repo
- [ai_docs/README.md](ai_docs/README.md) — canonical AI-doc routing index

## Support

- Bugs and feature requests: [GitHub Issues](https://github.com/darylmcd/Roslyn-Backed-MCP/issues)
- Contribution guidelines: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security disclosures: [SECURITY.md](SECURITY.md)
