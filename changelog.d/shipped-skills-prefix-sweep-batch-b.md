---
category: Fixed
---

- **Fixed:** Fixed the connectivity precheck in four more shipped skills (`inheritance-explorer`, `modernize`, `nuget-preflight`, `review`) so they resolve the Roslyn MCP tool prefix at runtime instead of hard-coding `mcp__roslyn__`. Marketplace-plugin installs surface the server under `mcp__plugin_roslyn-mcp_roslyn__`, so the old literal-name probe could halt these skills for legitimate users. Each now scans for a tool whose name ends in `server_info`, identifies Roslyn by response shape, and pins that prefix for every later call.
