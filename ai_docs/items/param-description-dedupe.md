# param-description-dedupe — Parameter-description dedupe across the tool surface

**row:** `param-description-dedupe` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/*.cs` (723 parameter `[Description]`s across 54 files — L split-candidate; split per file-cluster before planning)
- `tests/RoslynMcp.Tests/` (param-description ceiling/regression per slice)

## Acceptance

- [ ] `workspaceId` / `filePath` / common-boilerplate params standardized to one terse canonical one-liner each (repeated across essentially all 173 tools today)
- [ ] Total param-description chars cut ~40% (58,679 → ~35k; ~5.9k est. tokens)
- [ ] Semantically load-bearing param descriptions (discriminators, format contracts, preview-token semantics) NOT trimmed — parameter guidance drives call accuracy (Anthropic input_examples data: 72%→90%)

## Evidence

- Schema-property descriptions = 58,679 chars ≈ 14.7k tokens (27% of tools/list), avg ~81 chars across 723 described params — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §1

## Notes

- Original audit synthesis also proposed trimming `title` annotations (~1.9k tokens) — WRONG: the wire probe's per-tool annotations (89–91 chars) are exactly the four boolean hints; no annotation titles exist. Scope is param descriptions only.
## Amendment — 2026-08-25 (backlog-sweep 20260825T151721Z; PRs #1349, #1350 — row stays OPEN)

Two per-cluster child initiatives shipped; the row stays open for the remaining ~46 `Tools/*.cs` files.

**Shipped clusters:**
- **#1349** `EditTools`, `FileOperationTools`, `MSBuildTools`, `TypeMoveTools`
- **#1350** `CodeActionTools`, `SuppressionTools`, `SymbolRefactorTools`, `BulkRefactoringTools`

**Scheduling note for the next slices.** These clusters were chosen to be **disjoint from the `method-description-diet` clusters** running in the same sweep — the two families edit the same files (method-level vs parameter-level `[Description]`), so overlapping clusters would have produced conflict edges and serialized the waves. Keep partitioning both families against one shared file map.

**Advisory findings from cold review (not blockers):** the canonicalization ratchet could assert exact strings rather than shape, and the guard could be generalized via `ToolParameterIndex` instead of per-slice type lists — worth folding into the next slice rather than filing as siblings, since they edit the same test surface.
