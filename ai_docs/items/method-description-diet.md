# method-description-diet — Method-level description diet across the tool surface

**row:** `method-description-diet` · **pri:** `Medium` · **size:** `L` · **deps:** `server-instructions-discovery-hint`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/*.cs` (54 files carry `[McpServerTool]` methods — L split-candidate; split per category/file-cluster before planning)
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (workspace_load — top offender after semantic_grep)
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs` (compile_check)
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` (server_info; also find_references, find_duplicated_methods in their files)
- `tests/RoslynMcp.Tests/` (description-length ceiling test, tightened from the 2,000-char defect cap toward the diet target)

## Acceptance

- [ ] Method Descriptions become ~150–200-char capability statements (what it does, when to use); usage guidance/warnings relocated to ServerInstructions, prompts (via get_prompt_text), or the paginated catalog resource
- [ ] Total method-description chars ≤ ~40k (from 68,408) — ~8.5k est. token cut on tools/list
- [ ] No capability statement dropped — each tool still names its discriminating trigger vs sibling tools (ToolSearch discovery depends on it)
- [ ] Per-slice regression: description-length ceiling test ratcheted down as slices land

## Evidence

- Method-level descriptions = 68,408 chars ≈ 17.1k tokens (31% of tools/list); avg 393 chars/tool — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §1

## Notes

- Depends on `server-instructions-discovery-hint` (the relocation target must exist before guidance moves).
- Benefits ALL clients; deferring clients also pay less per on-demand tool load (~318 avg tokens/tool today).
