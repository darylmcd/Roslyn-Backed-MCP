# preview-token-store-mismatch-false-stale — Route preview tokens to the right store and document each preview's apply tool

**row:** `preview-token-store-mismatch-false-stale` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:394`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:67`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:169`

## Acceptance

- [ ] apply_composite_preview(<scaffold_test_batch token>) either applies it or names preview_multi_file_edit_apply
- [ ] workspace_fork_apply accepts project-mutation tokens or its description drops 'any *_preview'
- [ ] Descriptions of scaffold_test_batch_preview, dependency_inversion_preview, extract_interface_cross_project_preview, move_type_to_project_preview, symbol_refactor_preview name their apply tool
- [ ] Re-applying a consumed token says the token was already applied, not that the workspace reloaded

## Evidence

- scaffold_test_batch_preview token → apply_composite_preview 'PreviewTokenStale: workspace was reloaded'; same token → preview_multi_file_edit_apply succeeds (3 files). symbol_refactor token → preview_multi_file_edit_apply reported 'reloaded'. Consumed token: rename_apply(token) twice → 2nd call "expired because the workspace was reloaded" (it was consumed). — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
