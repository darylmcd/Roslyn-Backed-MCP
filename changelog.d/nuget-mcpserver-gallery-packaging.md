---
category: Added
---

- **Added:** `Darylmcd.RoslynMcp` now lists on the NuGet MCP gallery. The package carries the `McpServer` package type alongside `DotnetTool` and embeds the canonical `.mcp/server.json` manifest (single source of truth: `.claude-plugin/server.json`), so it surfaces under nuget.org `?packagetype=mcpserver` with an "MCP Server" tab plus the VS/VS Code "copy MCP config" experience, while `dotnet tool install -g` / `dnx` continue to work unchanged. Closes `nuget-mcpserver-gallery-packaging`.
