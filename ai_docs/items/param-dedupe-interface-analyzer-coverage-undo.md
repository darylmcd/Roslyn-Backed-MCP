# param-dedupe-interface-analyzer-coverage-undo — parameter-description dedupe: Interface-member, analyzer and coverage slice

**row:** `param-dedupe-interface-analyzer-coverage-undo` · **pri:** `Low` · **size:** `M` · **deps:** `desc-budget-harness-param-family`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/RemoveInterfaceMemberTool.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalyzerInfoTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs`
- `tests/RoslynMcp.Tests/ParamDescriptionCanonicalInterfaceAnalyzerCoverageUndoTests.cs` (new — calls the shared harness)

## Acceptance

- [ ] `workspaceId` / `filePath` / common-boilerplate parameter `[Description]`s in the listed files are standardized to the one canonical terse one-liner each already used by the shipped slices.
- [ ] Semantically load-bearing parameter descriptions — discriminators, format contracts, preview-token semantics — are NOT trimmed; parameter guidance drives call accuracy.
- [ ] One slice canonicalization ratchet asserts the canonical form for boilerplate params and leaves load-bearing ones free, calling the SHARED harness from `desc-budget-harness-param-family` rather than a new copy.

## Evidence

Measured on `main` 2026-09-02: this slice carries **16 parameter `[Description]`s totalling 820 chars** across 4 file(s). Whole-surface uncovered remainder at split time: 36 files, 528 params, 41,760 chars; 16 files (186 params, 12,774 chars) are already covered by the two shipped canonicalization ratchets (PRs #1349, #1350).

## Context

Split from `param-description-dedupe` (2026-09-02).

**Advisory findings from the shipped slices' cold reviews, to fold in here rather than file as siblings:** the canonicalization ratchet could assert exact strings rather than shape, and the guard could be generalized via a `ToolParameterIndex` instead of per-slice type lists.

Partition against the `method-diet-*` family's file map — the two families edit the same files (parameter-level vs method-level `[Description]`), so overlapping clusters produce conflict edges and serialize the waves. This was done deliberately for the August sweep and should continue.
