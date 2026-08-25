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
## Amendment — 2026-08-25 (backlog-sweep 20260825T151721Z; PRs #1343, #1346, #1348, #1345 — row stays OPEN)

Four per-cluster child initiatives shipped; the row stays open for the remaining ~40 `Tools/*.cs` files.

**Shipped clusters (disjoint file sets, chosen so the method-diet and param-dedupe families could run concurrently without conflict):**
- **#1343** `SymbolTools`, `AdvancedAnalysisTools`, `AnalysisTools`
- **#1346** `WorkspaceTools`, `ValidationTools`, `ValidationBundleTools`, `CompileCheckTools`
- **#1348** `ExtractMethodTools`, `ChangeSignatureTools`, `ParameterObjectTools`, `SyntaxTools`
- **#1345** `ScaffoldingTools`, `ProjectMutationTools`, `MultiFileEditTools`, `CrossProjectRefactoringTools`

**Lesson for the next slices — the per-tool figure is a CEILING, not a target.** #1345's first attempt asserted a 2,800-char slice aggregate against a spec'd 2,200, justified in-code by claiming ~2,794 was the "theoretical minimum". That reasoning was unsound: it multiplied the ~200-char per-tool ceiling by the tool count as though it were a floor. Cold spec review caught it; the corrected slice measures **2,019** with every description still under the per-tool ceiling and every discriminating trigger retained. Budget future slices from real per-tool content, not from `toolCount × ceiling`.

**Also observed:** #1348's first attempt dropped the refusal clause from `extract_shared_expression_to_helper_preview` entirely where the Approach called for condensing it — i.e. a capability statement was lost, which this row's acceptance forbids. Cold review caught that too. Both are worth briefing into the next slice.

**Follow-ons filed separately:** per-slice ceiling ratchets are accumulating one test class per slice; consolidating them into one data-driven test is worth doing before the remaining ~40 files are swept.
