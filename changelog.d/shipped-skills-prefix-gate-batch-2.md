---
category: Fixed
---

- **Fixed:** the shipped `analyze`, `architecture-review`, `complexity`, and `di-audit` skills no longer hard-stop on a marketplace-plugin install. Their connectivity precheck now resolves the Roslyn MCP server by calling each tool whose name ends in `server_info` and identifying the Roslyn one by response shape, then pins that prefix for every later call — so the gate works under both the dev-build (`mcp__roslyn__*`) and plugin (`mcp__plugin_roslyn-mcp_roslyn__*`) registration paths. Part of an ongoing sweep; 13 shipped skill files remain.
