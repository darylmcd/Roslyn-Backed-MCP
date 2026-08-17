# MCP Client Compatibility Matrix

The Roslyn-Backed MCP server uses the standard MCP stdio transport and explicitly supports the protocol eras recorded in [ADR 0003](decisions/0003-sdk-2x-wire-compatibility.md). A client must support both stdio and one of those protocol contracts; this matrix records the install paths actually exercised plus known gotchas. PRs welcome to flip rows from *Likely* to *Tested* with evidence.

## Status legend

| Status | Meaning |
|--------|---------|
| **Tested** | I have personally completed install → handshake → at least one tool call against this client and version. Evidence link in the Notes column. |
| **Likely** | Same MCP client family as a Tested row, or matches the documented stdio-MCP contract; no reason to expect breakage but I have not verified end-to-end. |
| **Untested** | No verification. Listed for completeness; no claim made. |

## Clients

| Client | Config file | Status | Notes |
|--------|-------------|--------|-------|
| Claude Code (CLI + IDE extensions) | `.mcp.json` (project root) or `~/.claude/mcp.json` (global) | **Tested** | Primary development client. Both global-tool and `dnx` install paths exercised. The Claude Code Plugin path ([`/plugin install roslyn-mcp@roslyn-mcp-marketplace`](../README.md#option-c--claude-code-plugin)) is the highest-fidelity install — bundles the server, 32 skills, and safety hooks. |
| Cursor | `.cursor/mcp.json` (project) or `~/.cursor/mcp.json` (global) | **Likely** | Standard stdio MCP host; uses the same JSON shape as Claude Code. Project-scope wins over global. |
| VS Code (MCP-aware extensions) | `.vscode/mcp.json` (workspace) | **Likely** | Visual Studio Code's first-party MCP support and several MCP-host extensions consume the same stdio config shape. Restart the MCP host after editing. |
| Claude Desktop | `claude_desktop_config.json` (per-OS app-data dir) | **Likely** | macOS path: `~/Library/Application Support/Claude/`. Windows path: `%APPDATA%\Claude\`. Global only — no project-scope config. |
| Other stdio MCP clients (custom Python / TypeScript) | client-defined | **Likely** | Server supports initialization-based legacy sessions through 2025-11-25 and the modern 2026-07-28 discovery/request flow over stdio with NDJSON framing. See [docs/stdio-client-integration.md](stdio-client-integration.md) for handshake order and minimal Python / C# client examples. |

## Install-path × client compatibility

The three install paths from the [README Quick Start](../README.md#quick-start) are independent of the client. Pick whichever fits your environment:

| Install path | Works with | Cold-start cost | Update story |
|--------------|------------|-----------------|--------------|
| **Option A** — `dotnet tool install -g Darylmcd.RoslynMcp` | All clients in the matrix above | Lowest (already on disk) | Manual: `dotnet tool update -g Darylmcd.RoslynMcp` |
| **Option B** — `dnx Darylmcd.RoslynMcp --yes` (.NET 10 SDK Preview 6+) | All clients in the matrix above | Higher on first launch (NuGet resolve) | Implicit: each cold start resolves to latest unless `--version` pinned |
| **Option C** — Claude Code plugin (`/plugin install ...`) | Claude Code only | Lowest (bundled) | `/plugin update` from the Claude Code prompt |

## Known issues

- **Plugin install on Windows**: the `/plugin install` UI can silently fail on the rename step (EPERM in the Node installer). Workaround: drop to CLI (`claude plugin install roslyn-mcp@roslyn-mcp-marketplace`), which surfaces the actual error. See [docs/setup.md](setup.md) and [docs/reinstall.md](reinstall.md) for the manual rename + re-register dance.
- **`dnx` requires .NET 10 SDK Preview 6 or later**. Older SDKs do not ship the `dnx` command. Run `dotnet --list-sdks` to confirm.
- **`--yes` is mandatory under MCP hosts**. Without it, `dnx` prompts for install consent on stdin, which the host cannot answer; the process appears to hang and never produces an MCP `initialize` response.

## First Workspace Check

After `workspace_load`, call `workspace_readiness_report` for onboarding/first-run status. It returns a compact verdict (`ready`, `restore-needed`, `build-needed`, or `analyzer-limited`), limitations, and next workflows without running tests by default. Use `workspace_support_bundle` separately for incident or recovery diagnostics.

## Reporting compatibility findings

If you successfully wire up a client not listed here, or find a real incompatibility, file via the [Surface-test finding](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/new?template=mcp-server-surface-test-finding.yml) issue template (or a regular bug report).
