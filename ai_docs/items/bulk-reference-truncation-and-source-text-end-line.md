# bulk-reference-truncation-and-source-text-end-line — Make truncated read results consistent with their paging fields

**row:** `bulk-reference-truncation-and-source-text-end-line` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`

## Acceptance

- [ ] find_references_bulk with maxItemsPerSymbol=N returns the same first N as `find_references(offset=0, limit=N)`.
- [ ] get_source_text with a maxChars truncation reports returnedEndLine as the last line actually returned.

## Evidence

- G3-F1 and G3-F2.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
