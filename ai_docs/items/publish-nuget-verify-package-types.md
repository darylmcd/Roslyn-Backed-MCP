# publish-nuget-verify-package-types — assert McpServer type + embedded manifest in the packed nupkg

**row:** `publish-nuget-verify-package-types` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/workflows/publish-nuget.yml` — the "Verify package contents" step (~lines 49-57)
- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` — `<PackageType>McpServer</PackageType>` + the `.mcp/server.json` `<None Include>` (the regression surface)

## Acceptance

- [ ] The "Verify package contents" step unzips the packed `.nupkg` and fails the run unless the nuspec contains `<packageType name="McpServer" />` AND the archive contains `.mcp/server.json`.
- [ ] A deliberate check (removing either csproj line) confirms the step goes red instead of shipping a tool-only package on green CI.

## Evidence

- Code-quality review of `nuget-mcpserver-gallery-packaging` (2026-06-19 top-n-remediation): the verify step today only checks `.nupkg`/`.snupkg` exist by size; it never asserts the new `McpServer` package type or the embedded `.mcp/server.json` are present. A future csproj regression (someone drops `<PackageType>` or the `<None Include>` line) would ship a tool-only package with green CI, silently delisting from the NuGet MCP gallery.

## Context

The NuGet MCP gallery listing now depends on the csproj staying correct, with no CI guard. `eng/verify-version-drift.ps1` covers the manifest *version* but not its *presence in the package*. This row adds the missing presence/type assertion to the release pack verification.
