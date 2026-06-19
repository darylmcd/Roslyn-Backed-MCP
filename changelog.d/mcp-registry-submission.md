---
category: Added
---

- **Added:** Automated publishing to the official [MCP Registry](https://registry.modelcontextprotocol.io/) (`io.github.darylmcd/roslyn-mcp`) on release tags via GitHub OIDC — the `publish-nuget` workflow now publishes `server.json` once the NuGet package is live. Adds the registry ownership marker to the packaged README, trims the manifest `description` to the 100-char schema limit, and hardens `eng/verify-registry-readiness.ps1` to catch both gaps. Closes `mcp-registry-submission`.
