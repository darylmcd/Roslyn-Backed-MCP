# nuget-mcpserver-gallery-packaging — publish to the NuGet MCP gallery

**row:** `nuget-mcpserver-gallery-packaging` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj:13-15` (`PackAsTool` / `ToolCommandName` / `PackageId`)
- `.claude-plugin/server.json` (source manifest to embed at `.mcp/server.json`)
- `.github/workflows/publish-nuget.yml` (pack step — verify both package types ship)

## Acceptance

- [ ] `<PackageType>McpServer</PackageType>` added alongside `<PackAsTool>true</PackAsTool>` (MS docs: `McpServer` is always accompanied by `DotnetTool`; the `dotnet new mcpserver` template ships exactly this pair).
- [ ] `.mcp/server.json` embedded in the package (pack the existing `.claude-plugin/server.json` to `PackagePath=.mcp/server.json`, single source of truth).
- [ ] `Darylmcd.RoslynMcp` appears under nuget.org `?packagetype=mcpserver` with an "MCP Server" tab; `dotnet tool install -g` and `dnx` still work (DotnetTool type preserved).
- [ ] Pack validated in a PR (the `dotnet tool install` path must not regress).

## Evidence

- Deferred from the 2026-06-19 registry-publish PR (#976): this is a SEPARATE discovery surface (NuGet's own MCP gallery) from the official MCP Registry, and it perturbs the published artifact's package types, so it warrants its own pack-tested PR rather than riding a release.

## Context

Distinct from the official MCP Registry (already live via #976). This is the nuget.org-native gallery + the VS Code/Visual Studio "copy MCP config" experience.
