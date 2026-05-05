---
category: Changed
---

- **Changed:** `ai_docs/prompts/deep-review-and-refactor.md` (the prompt resolved by `/audit-deep`) now names 10 previously-unmapped MCP tools across the live surface — `workspace_health` in Phase -1, `revert_apply_by_sequence` in Phase 9, `semantic_grep` in Phase 11, `get_test_coverage_map` in Phase 8, `find_dead_fields` / `find_duplicated_code` / `find_type_consumers` in Phases 2–3, `get_symbol_outline` in Phase 14, `scaffold_test_apply` / `scaffold_type_apply` in Phase 12. Adds a Phase-0 Live-surface drift-detection sub-step that diffs the live catalog against names mentioned in the prompt and FAILs P1 on stale references. The `/audit-deep` slash-command description was rewritten to match the actual deliverable (Roslyn MCP server audit + promotion scorecard + plugin-skill audit, not a generic codebase refactor).
