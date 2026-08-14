---
category: Fixed
---

- **Fixed:** the NuGet package's `.mcp/server.json` now declares `ROSLYNMCP_SANCTIONED_ROOTS=.` via the registry schema's `environmentVariables`, so `dnx` and registry-aware clients configure the filesystem boundary automatically instead of starting from an empty (fail-closed) boundary that rejects every path-taking tool. This brings the discovery-driven NuGet path to parity with the Claude Code plugin (`.claude-plugin/mcp.json`) and the Desktop extension (`manifest.json`), both of which already shipped the default. Note the residual: a plain `dotnet tool install -g` followed by a hand-written `mcp.json` does not read `server.json`, so that path still requires configuring the variable explicitly — see the README's Configuration section.
