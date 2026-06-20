---
category: Maintenance
---

- **Maintenance:** The `publish-nuget.yml` "Verify package contents" step now unzips the packed `.nupkg` and fails the release unless the nuspec carries `<packageType name="McpServer" />` **and** the archive embeds `.mcp/server.json` — so a csproj regression dropping either would no longer ship a tool-only package on green CI and silently delist from the NuGet MCP gallery. Previously the step only size-checked the package (`publish-nuget-verify-package-types`).
