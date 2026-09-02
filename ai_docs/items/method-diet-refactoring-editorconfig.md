# method-diet-refactoring-editorconfig — method-description diet: Refactoring, editorconfig and type-move slice

**row:** `method-diet-refactoring-editorconfig` · **pri:** `Medium` · **size:** `M` · **deps:** `desc-budget-harness-method-family`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/DeadCodeTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietRefactoringEditorconfigTests.cs` (new — calls the shared harness)

## Acceptance

- [ ] Every `[McpServerTool]` method `[Description]` in the listed files is a capability statement of ~150–200 chars (what it does, when to use); operational guidance moves to XML `<remarks>` on the same method, ServerInstructions, or a prompt.
- [ ] No capability statement is dropped — each tool still names its discriminating trigger versus sibling tools (ToolSearch discovery depends on it).
- [ ] One slice ratchet asserts the per-tool ceiling, the slice total, non-empty descriptions, and this slice's discriminating-trigger substrings — calling the SHARED harness from `desc-budget-harness-method-family`, not a new ~98-line copy.

## Evidence

Measured on `main` 2026-09-02 (whole surface: 174 tools, 35,135 method-description chars):

| File | tools | total chars | max | over 200 |
|---|---|---|---|---|
| `RefactoringTools.cs` | 11 | 1615 | 511 | 2 |
| `EditorConfigTools.cs` | 2 | 721 | 548 | 1 |
| `DeadCodeTools.cs` | 2 | 515 | 458 | 1 |
| `TypeMoveTools.cs` | 3 | 600 | 297 | 2 |

Slice excess over the 200-char ceiling: **1208 chars** (~1208 excess chars to remove). `RefactoringTools` carries 11 tools but only 2 over the ceiling; the excess is concentrated in single outliers (511 / 548 / 458 / 297 chars).

## Context

Split from `method-description-diet` (2026-09-02). 4 of the 26 `Tools/*.cs` files that still carry an over-ceiling method description; 28 files are already ratcheted by the four slices shipped in August (PRs #1343, #1346, #1348, #1345).

**The umbrella's total-chars acceptance bullet is ALREADY MET** — 35,135 chars against its "≤ ~40k (from 68,408)" target. What remains is the per-tool ceiling and the discriminating-trigger guarantee, which is what these children own.

**Two lessons carried forward from the shipped slices (both caught by cold review, both worth re-reading before starting):**
- The per-tool figure is a **CEILING, not a target**. #1345's first attempt asserted a 2,800-char slice aggregate justified as `toolCount × ceiling` being a "theoretical minimum" — unsound; the corrected slice measured 2,019. Budget from real per-tool content.
- #1348's first attempt **dropped the refusal clause** from `extract_shared_expression_to_helper_preview` where the approach called for condensing it. Losing a capability statement is forbidden by acceptance bullet 2.

Partition both description families (`method-diet-*` and `param-dedupe-*`) against one shared file map — they edit the same files (method-level vs parameter-level `[Description]`), so overlapping clusters produce conflict edges and serialize the waves.
