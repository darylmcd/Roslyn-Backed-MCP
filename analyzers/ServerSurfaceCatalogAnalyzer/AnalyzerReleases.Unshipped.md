; Unshipped analyzer release.
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category  | Severity | Notes
--------|-----------|----------|-------
RMCP001 | McpCatalog | Warning | SurfaceCatalog missing entry for an `[McpServer*]`-attributed method.
RMCP002 | McpCatalog | Warning | SurfaceCatalog entry is not backed by any `[McpServer*]` attribute.
RMCP010 | McpHostStdio | Warning | Direct stdout write in `RoslynMcp.Host.Stdio` (would corrupt MCP NDJSON framing).
