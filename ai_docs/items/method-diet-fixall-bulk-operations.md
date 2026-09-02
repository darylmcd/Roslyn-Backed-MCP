# method-diet-fixall-bulk-operations — method-description diet: Fix-all, bulk and operations slice

**row:** `method-diet-fixall-bulk-operations` · **pri:** `Medium` · **size:** `M` · **deps:** `desc-budget-harness-method-family`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/BulkRefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/OperationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScriptingTools.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietFixallBulkOperationsTests.cs` (new — calls the shared harness)

## Acceptance

- [ ] Every `[McpServerTool]` method `[Description]` in the listed files is a capability statement of ~150–200 chars (what it does, when to use); operational guidance moves to XML `<remarks>` on the same method, ServerInstructions, or a prompt.
- [ ] No capability statement is dropped — each tool still names its discriminating trigger versus sibling tools (ToolSearch discovery depends on it).
- [ ] One slice ratchet asserts the per-tool ceiling, the slice total, non-empty descriptions, and this slice's discriminating-trigger substrings — calling the SHARED harness from `desc-budget-harness-method-family`, not a new ~98-line copy.

## Evidence

Measured on `main` 2026-09-02 (whole surface: 174 tools, 35,135 method-description chars):

| File | tools | total chars | max | over 200 |
|---|---|---|---|---|
| `FixAllTools.cs` | 2 | 807 | 737 | 1 |
| `BulkRefactoringTools.cs` | 3 | 1002 | 535 | 2 |
| `OperationTools.cs` | 1 | 691 | 691 | 1 |
| `ScriptingTools.cs` | 1 | 676 | 676 | 1 |

Slice excess over the 200-char ceiling: **2025 chars** (~2025 excess chars to remove). Four files with one oversized description each (737 / 535 / 691 / 676 chars).

## Context

Split from `method-description-diet` (2026-09-02). 4 of the 26 `Tools/*.cs` files that still carry an over-ceiling method description; 28 files are already ratcheted by the four slices shipped in August (PRs #1343, #1346, #1348, #1345).

**The umbrella's total-chars acceptance bullet is ALREADY MET** — 35,135 chars against its "≤ ~40k (from 68,408)" target. What remains is the per-tool ceiling and the discriminating-trigger guarantee, which is what these children own.

**Two lessons carried forward from the shipped slices (both caught by cold review, both worth re-reading before starting):**
- The per-tool figure is a **CEILING, not a target**. #1345's first attempt asserted a 2,800-char slice aggregate justified as `toolCount × ceiling` being a "theoretical minimum" — unsound; the corrected slice measured 2,019. Budget from real per-tool content.
- #1348's first attempt **dropped the refusal clause** from `extract_shared_expression_to_helper_preview` where the approach called for condensing it. Losing a capability statement is forbidden by acceptance bullet 2.

Partition both description families (`method-diet-*` and `param-dedupe-*`) against one shared file map — they edit the same files (method-level vs parameter-level `[Description]`), so overlapping clusters produce conflict edges and serialize the waves.
