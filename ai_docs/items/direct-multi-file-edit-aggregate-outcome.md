# direct-multi-file-edit-aggregate-outcome — Report the actual outcome of direct edit batches

**row:** `direct-multi-file-edit-aggregate-outcome` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Verify actual disk writes, the experimental apply_multi_file_edit success/count result and ledger for mixed and entirely refused batches. Count only successful writes and preserve truthful per-file results and recovery for earlier writes.

## Evidence

- Cold review of repeated-target preflight found that direct apply returns true with results.Count even when syntax validation records zero edits or persistence returns a failure. The change ledger also includes every requested file. Source-derived.

## Notes

5.x screen at d9e2b3ff. The direct batch route is experimental, but ApplyTextEditsCoreAsync calls TryApplyChanges before its disk fallback. A false result after that point can coexist with a native write or an in-memory change. It also serves stable apply_text_edit. Filtering summaries alone leaves incorrect recovery and ledger effects. Select after confirming a complete compensation and receipt fix preserves the stable apply_text_edit and workspace_changes contracts, or at the next approved major.
