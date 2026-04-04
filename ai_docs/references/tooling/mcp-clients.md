# MCP Client Tooling Reference

<!-- purpose: How MCP clients (e.g. Cursor) connect to this server and policy pointers. -->

## Cursor

- This repository ships **`.mcp.json`** at the repo root with a `roslyn` server (`command: roslynmcp`, stdio). Cursor loads project MCP config when present; you can mirror the same entry in `.cursor/mcp.json` or user MCP settings if needed.
- Prefer the published `roslynmcp` executable for lower startup overhead when repeatedly launching.
- AI agents should follow **`ai_docs/runtime.md`** (*Roslyn MCP client policy*): use the server for C# **refactoring**, not only discovery.

## Claude Code

Configure MCP settings with the same command/args model used by Cursor.

## Operational Guidance

- Prefer published executable for lower startup overhead when repeatedly launching.
- Keep command paths machine-local and avoid committing personal absolute paths.
