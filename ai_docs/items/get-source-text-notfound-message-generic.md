# get-source-text-notfound-message-generic — Return a file-specific NotFound for get_source_text and location-based lookups

**row:** `get-source-text-notfound-message-generic` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:568`

## Acceptance

- [ ] get_source_text{filePath:'C:/nonexistent/x.cs'} names the file-not-in-workspace cause
- [ ] Path traversal inputs still reveal nothing about the host filesystem

## Evidence

- get_source_text nonexistent path → "The requested item was not found. Ensure the workspace is loaded and the identifier is correct." — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
