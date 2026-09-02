# method-diet-ratchet-only-clean-slices — method-description diet: Ratchet-only slice (already compliant)

**row:** `method-diet-ratchet-only-clean-slices` · **pri:** `Medium` · **size:** `M` · **deps:** `desc-budget-harness-method-family`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietRatchetOnlyCleanSlicesTests.cs` (new — calls the shared harness)

## Acceptance

- [ ] Every `[McpServerTool]` method `[Description]` in the listed files is a capability statement of ~150–200 chars (what it does, when to use); operational guidance moves to XML `<remarks>` on the same method, ServerInstructions, or a prompt.
- [ ] No capability statement is dropped — each tool still names its discriminating trigger versus sibling tools (ToolSearch discovery depends on it).
- [ ] One slice ratchet asserts the per-tool ceiling, the slice total, non-empty descriptions, and this slice's discriminating-trigger substrings — calling the SHARED harness from `desc-budget-harness-method-family`, not a new ~98-line copy.

## Evidence

Measured on `main` 2026-09-02 (whole surface: 174 tools, 35,135 method-description chars):

| File | tools | total chars | max | over 200 |
|---|---|---|---|---|
| `CodeActionTools.cs` | 3 | 296 | 134 | 0 |
| `FileOperationTools.cs` | 6 | 523 | 112 | 0 |
| `OrchestrationTools.cs` | 4 | 451 | 180 | 0 |
| `TypeExtractionTools.cs` | 2 | 229 | 185 | 0 |

Slice excess over the 200-char ceiling: **0 chars** (no production edits expected). ZERO production edits required — every tool in these four files is already under the ceiling (max 185 chars). Deliverable is the missing ratchet test only, so the files cannot silently regrow.

## Context

Split from `method-description-diet` (2026-09-02). 4 of the 26 `Tools/*.cs` files that still carry an over-ceiling method description; 28 files are already ratcheted by the four slices shipped in August (PRs #1343, #1346, #1348, #1345).

**The umbrella's total-chars acceptance bullet is ALREADY MET** — 35,135 chars against its "≤ ~40k (from 68,408)" target. What remains is the per-tool ceiling and the discriminating-trigger guarantee, which is what these children own.

**Two lessons carried forward from the shipped slices (both caught by cold review, both worth re-reading before starting):**
- The per-tool figure is a **CEILING, not a target**. #1345's first attempt asserted a 2,800-char slice aggregate justified as `toolCount × ceiling` being a "theoretical minimum" — unsound; the corrected slice measured 2,019. Budget from real per-tool content.
- #1348's first attempt **dropped the refusal clause** from `extract_shared_expression_to_helper_preview` where the approach called for condensing it. Losing a capability statement is forbidden by acceptance bullet 2.

Partition both description families (`method-diet-*` and `param-dedupe-*`) against one shared file map — they edit the same files (method-level vs parameter-level `[Description]`), so overlapping clusters produce conflict edges and serialize the waves.
