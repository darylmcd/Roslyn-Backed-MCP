# MCP Client Compatibility Matrix

The Roslyn-Backed MCP server uses the standard MCP stdio transport and explicitly supports the protocol eras recorded in [ADR 0003](decisions/0003-sdk-2x-wire-compatibility.md). [ADR 0006](decisions/0006-modelcontextprotocol-2-2-servicing.md) records the non-breaking SDK 2.2 servicing review and confirms that the separate ASP.NET Core hybrid-session feature is not part of this stdio product; [ADR 0031](decisions/0031-modelcontextprotocol-2-3-servicing.md) records the non-breaking SDK 2.3 servicing review. A client must support both stdio and one of those protocol contracts; this matrix records the install paths actually exercised plus known gotchas. PRs welcome to flip rows from *Likely* to *Tested* with evidence.

## Status legend

| Status | Meaning |
|--------|---------|
| **Tested** | Maintainers completed install → handshake → at least one tool call against this client. Not tied to a recorded client version or date. |
| **Likely** | Same MCP client family as a Tested row, or matches the documented stdio-MCP contract; no reason to expect breakage but not verified end-to-end. |
| **Untested** | No verification. Listed for completeness; no claim made. |

## Clients

| Client | Config file | Status | Notes |
|--------|-------------|--------|-------|
| Claude Code (CLI + IDE extensions) | `.mcp.json` (project root) or `~/.claude.json` (user scope, `claude mcp add -s user`) | **Tested** | Primary development client. Both global-tool and `dnx` install paths exercised. The Claude Code Plugin path ([`/plugin install roslyn-mcp@roslyn-mcp-marketplace`](../README.md#option-c--claude-code-plugin)) bundles 32 skills and advisory reminder hooks and launches the release-matched NuGet package through `dnx`. |
| Cursor | `.cursor/mcp.json` (project) or `~/.cursor/mcp.json` (global) | **Likely** | Standard stdio MCP host; uses the same JSON shape as Claude Code. Project-scope wins over global. |
| VS Code (MCP-aware extensions) | `.vscode/mcp.json` (workspace) | **Likely** | Visual Studio Code's first-party MCP support and several MCP-host extensions consume the same stdio server entry, but VS Code's `.vscode/mcp.json` uses a top-level `"servers"` key instead of `"mcpServers"`. Restart the MCP host after editing. |
| Claude Desktop | `claude_desktop_config.json` (per-OS app-data dir) | **Likely** | macOS path: `~/Library/Application Support/Claude/`. Windows path: `%APPDATA%\Claude\`. Global only — no project-scope config. |
| Other stdio MCP clients (custom Python / TypeScript) | client-defined | **Likely** | Server supports initialization-based legacy sessions through 2025-11-25 and the modern 2026-07-28 discovery/request flow over stdio with NDJSON framing. See [docs/stdio-client-integration.md](stdio-client-integration.md) for handshake order and minimal Python / C# client examples. |

## Install-path × client compatibility

The three install paths from the [README Quick Start](../README.md#quick-start) are independent of the client. Pick whichever fits your environment:

| Install path | Works with | Cold-start cost | Update story |
|--------------|------------|-----------------|--------------|
| **Option A** — `dotnet tool install -g Darylmcd.RoslynMcp` | All clients in the matrix above | Lowest (already on disk) | Manual: `dotnet tool update -g Darylmcd.RoslynMcp` |
| **Option B** — `dnx Darylmcd.RoslynMcp` (.NET 10 SDK 10.0.100+) | All clients in the matrix above | Higher on first launch (NuGet resolve) | Implicit: each cold start resolves to latest unless the package token uses `@<version>` |
| **Option C** — Claude Code plugin (`/plugin install ...`) | Claude Code only | Higher on first launch (pinned NuGet resolve; cached afterward) | Run the [reinstall.md](reinstall.md) sequence (or `/roslyn-mcp:update`) to advance the release-matched pin |

## Configuration snippets

The server-entry shape is the same across clients; only the file path (and VS Code's root key) differs. Minimal config for the global tool (Option A):

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "roslynmcp",
      "env": { "ROSLYNMCP_SANCTIONED_ROOTS": "." }
    }
  }
}
```

`ROSLYNMCP_SANCTIONED_ROOTS` is required: an unset boundary is fail-closed (see [setup](setup.md#configure-the-filesystem-boundary)). For the `dnx` form (Option B) use the snippet in [setup](setup.md#zero-install-via-dnx) or copy [`docs/mcp-json-examples/dnx.mcp.json`](mcp-json-examples/dnx.mcp.json).

## Health check

After installing and wiring up your client config, paste this single prompt into your MCP client to verify the server is reachable and report its surface:

> Call `server_info` and read the `roslyn://server/catalog` resource. Report back:
> 1. The server name and version.
> 2. The total tool / resource / prompt counts and their stable-vs-experimental split.
> 3. Whether any workspaces are currently loaded (and their IDs if so).
> 4. Any warnings or degraded-state flags, including `pathBoundary` (a boundary that does not cover your repo makes every path-taking tool fail closed).

If both calls succeed and the version matches what you installed, the install is healthy.

## Known issues

- **Plugin install on Windows**: the `/plugin install` UI can silently fail on the rename step (EPERM in the Node installer). Workaround: drop to CLI (`claude plugin install roslyn-mcp@roslyn-mcp-marketplace`), which surfaces the actual error. If the CLI install also fails, refresh the plugin with `pwsh ./eng/update-claude-plugin.ps1` as described in [docs/reinstall.md](reinstall.md).
- **`dnx` requires .NET 10 SDK 10.0.100 or later**. Older SDKs do not ship the `dnx` command. Run `dotnet --list-sdks` to confirm.
- **The plugin's first launch requires NuGet access unless its exact package version is already cached.** Package publication/indexing can briefly lag a new plugin release; retry after NuGet exposes the version.

## First Workspace Check

After `workspace_load`, call `workspace_readiness_report` for onboarding/first-run status. It returns a compact verdict (`ready`, `restore-needed`, `build-needed`, or `analyzer-limited`), limitations, and next workflows without running tests by default. Use `workspace_support_bundle` separately for incident or recovery diagnostics.

## Reporting compatibility findings

If you successfully wire up a client not listed here, or find a real incompatibility, file via the [Surface-test finding](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/new?template=mcp-server-surface-test-finding.yml) issue template (or a regular bug report).
