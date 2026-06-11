# revert-last-apply-single-slot-doc-warning — state the single-slot LIFO behaviour loudly

**row:** `revert-last-apply-single-slot-doc-warning` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:12` (`revert_last_apply`) + `:62` (`revert_apply_by_sequence`)

## Acceptance

- [ ] Single-slot behaviour stated loudly in the tool description, pointing to `revert_apply_by_sequence` as the multi-step path
- [ ] Regression (doc-shaped): description/catalog test asserts the single-slot warning + cross-pointer are present

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 9, server v2.3.1 (split from `docs-tool-naming-and-revert-scope`). Source: 2026-05-31 surface-test.

## Context

`revert_last_apply` is single-slot LIFO — after one revert it reports "nothing to revert" even with 20+ applies still in `workspace_changes`, a sharp footgun.
